namespace OpenRussiaRouter.E2ETests.Pages;

public sealed class NodesPage(IPage page, string baseUrl)
{
    private readonly string _url = $"{baseUrl.TrimEnd('/')}/admin/nodes";

    public async Task NavigateAsync()
    {
        await page.GotoAsync(_url);
        await page.WaitForSelectorAsync("[data-testid='btn-add-node']");
    }

    public async Task AddNodeAsync(string name, string endpointUrl, string tokenSecretName = "", int capacityWeight = 100)
    {
        await page.ClickAsync("[data-testid='btn-add-node']");
        await page.WaitForSelectorAsync("[data-testid='form-add-node']");

        await page.FillAsync("[data-testid='input-name']", name);
        await page.FillAsync("[data-testid='input-endpoint-url']", endpointUrl);
        await page.FillAsync("[data-testid='input-token-secret-name']", tokenSecretName);

        await page.ClickAsync("[data-testid='btn-submit-add']");
        await page.WaitForSelectorAsync($"[data-testid='node-row'][data-node-name='{name}']");
    }

    public ILocator GetNodeRow(string name) =>
        page.Locator($"[data-testid='node-row'][data-node-name='{name}']");

    public async Task<string> GetNodeStatusAsync(string name)
    {
        var status = await GetNodeRow(name)
            .Locator("[data-testid='node-status']")
            .InnerTextAsync();
        return status.Trim().ToLowerInvariant();
    }

    public async Task FetchModelsAsync(string name)
    {
        await GetNodeRow(name).Locator("[data-testid='btn-models']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='models-card']");
    }

    public async Task<IReadOnlyList<string>> GetFetchedModelIdsAsync()
    {
        var rows = page.Locator("[data-testid='model-row']");
        var count = await rows.CountAsync();
        var ids = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var id = await rows.Nth(i).GetAttributeAsync("data-model-id");
            if (id != null) ids.Add(id);
        }
        return ids;
    }

    public async Task DisableNodeAsync(string name)
    {
        page.Dialog += (_, d) => d.AcceptAsync();
        await GetNodeRow(name).Locator("[data-testid='btn-disable']").ClickAsync();
        await page.WaitForSelectorAsync($"[data-testid='node-row'][data-node-name='{name}'] [data-testid='btn-enable']");
    }

    public async Task EnableNodeAsync(string name)
    {
        page.Dialog += (_, d) => d.AcceptAsync();
        await GetNodeRow(name).Locator("[data-testid='btn-enable']").ClickAsync();
        await page.WaitForSelectorAsync($"[data-testid='node-row'][data-node-name='{name}'] [data-testid='btn-disable']");
    }

    public async Task DeleteNodeAsync(string name)
    {
        page.Dialog += (_, d) => d.AcceptAsync();
        await GetNodeRow(name).Locator("[data-testid='btn-delete']").ClickAsync();
        await page.WaitForSelectorAsync($"[data-testid='node-row'][data-node-name='{name}']",
            new() { State = WaitForSelectorState.Hidden });
    }

    public async Task<bool> NodeExistsAsync(string name)
    {
        return await page.Locator($"[data-testid='node-row'][data-node-name='{name}']").CountAsync() > 0;
    }

    /// <summary>
    /// Polls the page until the node status becomes "healthy", proving the health check connected.
    /// Refreshes the page every 5 seconds. Default timeout: 90 seconds (covers 2-3 check cycles).
    /// </summary>
    public async Task WaitForHealthyAsync(string name, int timeoutSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var status = await GetNodeStatusAsync(name);
            if (status == "healthy")
                return;

            await Task.Delay(TimeSpan.FromSeconds(5));
            await page.ReloadAsync();
            await page.WaitForSelectorAsync("[data-testid='btn-add-node']");
        }

        var lastStatus = await GetNodeStatusAsync(name);
        throw new TimeoutException(
            $"Node '{name}' did not become healthy within {timeoutSeconds}s. Last status: {lastStatus}");
    }
}
