using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenRussiaRouter.E2ETests.Pages;

namespace OpenRussiaRouter.E2ETests;

public sealed class NodeManagementTests(PlaywrightFixture fixture) : IClassFixture<PlaywrightFixture>
{
    // Real 9router node used for E2E — responds to /v1/models without auth
    private const string TestNodeEndpoint = "https://ai.web-point.top/";

    [Fact]
    public async Task NodeLifecycle_AddDisableEnableDelete_FullFlow()
    {
        if (!TestConfiguration.IsConfigured)
        {
            return;
        }

        var run = Guid.NewGuid().ToString("N")[..8];
        var nodeName = $"E2E Node {run}";

        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var loginPage = new LoginPage(page, TestConfiguration.BaseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(TestConfiguration.UserEmail!, TestConfiguration.UserPassword!);

        var nodesPage = new NodesPage(page, TestConfiguration.BaseUrl);
        await nodesPage.NavigateAsync();

        // 1. Add node
        await nodesPage.AddNodeAsync(nodeName, TestNodeEndpoint);
        Assert.True(await nodesPage.NodeExistsAsync(nodeName));

        // 2. Verify connection: wait for health check to mark node healthy
        //    (proves the ControlPlane can reach the node's /v1/models endpoint)
        await nodesPage.WaitForHealthyAsync(nodeName);
        Assert.Equal("healthy", await nodesPage.GetNodeStatusAsync(nodeName));

        // 3. Fetch models directly from node API
        await nodesPage.FetchModelsAsync(nodeName);
        var models = await nodesPage.GetFetchedModelIdsAsync();
        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.StartsWith("minimax/", StringComparison.OrdinalIgnoreCase));

        // 4. Verify end-to-end LLM response
        var testModel = models.First(m => m.StartsWith("minimax/", StringComparison.OrdinalIgnoreCase));
        await VerifyLlmResponseAsync(page, run, testModel);

        // Navigate back after leaving the nodes page for model + key setup
        await nodesPage.NavigateAsync();

        // 5. Disable
        await nodesPage.DisableNodeAsync(nodeName);
        Assert.Equal("disabled", await nodesPage.GetNodeStatusAsync(nodeName));

        // 6. Enable
        await nodesPage.EnableNodeAsync(nodeName);
        Assert.NotEqual("disabled", await nodesPage.GetNodeStatusAsync(nodeName));

        // 7. Delete
        await nodesPage.DeleteNodeAsync(nodeName);
        Assert.False(await nodesPage.NodeExistsAsync(nodeName));
    }

    /// <summary>
    /// Registers the model in the billing catalog (if absent), creates a temporary
    /// API key, calls POST /v1/chat/completions, asserts a non-empty assistant reply,
    /// then revokes the key.
    /// </summary>
    private async Task VerifyLlmResponseAsync(IPage page, string run, string model)
    {
        // 4a. Ensure model is registered in billing with zero price
        var modelsAdmin = new ModelsAdminPage(page, TestConfiguration.BaseUrl);
        await modelsAdmin.NavigateAsync();
        await modelsAdmin.EnsureModelAsync(model, model, inputPrice: 0m, outputPrice: 0m);

        // 4b. Create a short-lived API key
        var keysPage = new ApiKeysPage(page, TestConfiguration.BaseUrl);
        await keysPage.NavigateAsync();
        var keyName = $"E2E LLM {run}";
        var apiKey = await keysPage.CreateKeyAsync(keyName);
        Assert.StartsWith("orr_", apiKey);

        // 4c. POST to the API gateway and assert a real LLM answer comes back
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = new[] { new { role = "user", content = "Reply with exactly one word: pong" } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{TestConfiguration.BaseUrl}/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"LLM call failed: HTTP {(int)response.StatusCode}\n{responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        Assert.False(
            string.IsNullOrWhiteSpace(content),
            $"LLM returned empty content. Full body:\n{responseBody}");

        // 4d. Revoke the temporary key
        await keysPage.NavigateAsync();
        await keysPage.RevokeKeyAsync(keyName);
    }
}
