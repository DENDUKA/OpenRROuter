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

        // 4. Disable
        await nodesPage.DisableNodeAsync(nodeName);
        Assert.Equal("disabled", await nodesPage.GetNodeStatusAsync(nodeName));

        // 5. Enable
        await nodesPage.EnableNodeAsync(nodeName);
        Assert.NotEqual("disabled", await nodesPage.GetNodeStatusAsync(nodeName));

        // 6. Delete
        await nodesPage.DeleteNodeAsync(nodeName);
        Assert.False(await nodesPage.NodeExistsAsync(nodeName));
    }
}
