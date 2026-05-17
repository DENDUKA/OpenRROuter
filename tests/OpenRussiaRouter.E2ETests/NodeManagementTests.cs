using OpenRussiaRouter.E2ETests.Pages;

namespace OpenRussiaRouter.E2ETests;

public sealed class NodeManagementTests(PlaywrightFixture fixture) : IClassFixture<PlaywrightFixture>
{
    // Mock LLM node deployed in the dev k8s cluster — no auth, deterministic responses.
    // Reachable by control-plane and api-gateway pods as http://mock-llm-node/
    private const string TestNodeEndpoint = "http://mock-llm-node/";

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

        // 2. Verify the control-plane health worker can reach the node
        //    (proves ControlPlane → /v1/models → 200 → status=healthy)
        await nodesPage.WaitForHealthyAsync(nodeName);
        Assert.Equal("healthy", await nodesPage.GetNodeStatusAsync(nodeName));

        // 3. Disable
        await nodesPage.DisableNodeAsync(nodeName);
        Assert.Equal("disabled", await nodesPage.GetNodeStatusAsync(nodeName));

        // 4. Enable
        await nodesPage.EnableNodeAsync(nodeName);
        Assert.NotEqual("disabled", await nodesPage.GetNodeStatusAsync(nodeName));

        // 5. Delete
        await nodesPage.DeleteNodeAsync(nodeName);
        Assert.False(await nodesPage.NodeExistsAsync(nodeName));
    }
}
