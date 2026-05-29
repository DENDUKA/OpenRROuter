using System.Globalization;

namespace OpenRussiaRouter.E2ETests.Pages;

sealed class ModelsAdminPage(IPage page, string baseUrl)
{
    private readonly string _listUrl = $"{baseUrl.TrimEnd('/')}/admin/models";
    private readonly string _syncUrl = $"{baseUrl.TrimEnd('/')}/admin/models/sync";

    public async Task NavigateAsync()
    {
        await page.GotoAsync(_listUrl);
        await page.WaitForSelectorAsync("a[href*='action=add']");
    }

    /// <summary>
    /// Adds the model if it is not already in the billing catalog.
    /// </summary>
    public async Task EnsureModelAsync(
        string id,
        string displayName,
        decimal inputPrice = 0m,
        decimal outputPrice = 0m)
    {
        // Check existing rows: code elements in the model table contain model IDs
        var existingIds = await page.Locator("table tbody tr td:first-child code").AllInnerTextsAsync();
        if (existingIds.Any(c => c.Trim() == id))
            return;

        await page.GotoAsync($"{_listUrl}?action=add");
        await page.WaitForSelectorAsync("#Form_Id");

        await page.FillAsync("#Form_Id", id);
        await page.FillAsync("#Form_DisplayName", displayName);
        await page.FillAsync("#Form_InputPrice", inputPrice.ToString("F2", CultureInfo.InvariantCulture));
        await page.FillAsync("#Form_OutputPrice", outputPrice.ToString("F2", CultureInfo.InvariantCulture));

        await page.ClickAsync("button.btn-primary");

        // After save the handler does RedirectToPage() → URL drops ?action=add
        await page.WaitForURLAsync(
            url => !url.Contains("action="),
            new() { Timeout = 15_000 });

        // The model ID should now appear as a <code> cell in the list table
        await page.WaitForSelectorAsync($"code:text-is('{id}')");
    }

    // ── Sync helpers ──────────────────────────────────────────────────────────

    /// <summary>Clicks the "Sync from 9Router" link on the /admin/models list page.</summary>
    public async Task ClickSyncFromRouterLinkAsync()
    {
        await page.ClickAsync("a[href*='/admin/models/sync']");
        await page.WaitForURLAsync(url => url.Contains("/admin/models/sync"), new() { Timeout = 10_000 });
    }

    /// <summary>Navigates directly to /admin/models/sync and waits for the page to settle.</summary>
    public async Task NavigateToSyncAsync()
    {
        await page.GotoAsync(_syncUrl);
        // Either the new-models table appears, or the "no new models" message
        await page.WaitForSelectorAsync(
            "[data-testid='models-sync-new-table'], p.text-muted",
            new() { Timeout = 20_000 });
    }

    /// <summary>Waits until the "New On Nodes" sync table is visible (throws if absent).</summary>
    public async Task WaitForSyncTableAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='models-sync-new-table']", new() { Timeout = 20_000 });
    }

    /// <summary>Checks (selects) the import checkbox for the given upstream model ID.</summary>
    public async Task SelectRowByUpstreamIdAsync(string upstreamId)
    {
        await page
            .Locator($"[data-testid='models-sync-row'][data-upstream-id='{upstreamId}'] [data-testid='models-sync-checkbox']")
            .CheckAsync();
    }

    /// <summary>Fills the per-row input/output/cache price fields for the given upstream model.</summary>
    public async Task SetRowPricesAsync(string upstreamId, decimal inputPrice, decimal outputPrice, decimal cachePrice)
    {
        var row = page.Locator($"[data-testid='models-sync-row'][data-upstream-id='{upstreamId}']");
        await row.Locator("[data-testid='models-sync-row-input-price']")
            .FillAsync(inputPrice.ToString("F2", CultureInfo.InvariantCulture));
        await row.Locator("[data-testid='models-sync-row-output-price']")
            .FillAsync(outputPrice.ToString("F2", CultureInfo.InvariantCulture));
        await row.Locator("[data-testid='models-sync-row-cache-price']")
            .FillAsync(cachePrice.ToString("F2", CultureInfo.InvariantCulture));
    }

    /// <summary>Checks or unchecks the per-row Enabled checkbox for the given upstream model.</summary>
    public async Task SetRowEnabledAsync(string upstreamId, bool enable)
    {
        var cb = page.Locator(
            $"[data-testid='models-sync-row'][data-upstream-id='{upstreamId}'] [data-testid='models-sync-row-enabled']");
        if (enable)
            await cb.CheckAsync();
        else
            await cb.UncheckAsync();
    }

    /// <summary>Clicks the "Import Selected" submit button.</summary>
    public async Task SubmitImportAsync()
    {
        await page.ClickAsync("[data-testid='models-sync-submit']");
    }

    /// <summary>Returns all model IDs (code cells in first column) from the /admin/models list table.</summary>
    public async Task<IReadOnlyList<string>> GetModelIdsAsync()
    {
        var texts = await page.Locator("table tbody tr td:first-child code").AllInnerTextsAsync();
        return texts.Select(t => t.Trim()).ToList();
    }

    /// <summary>Returns true if a success alert with the given text is visible.</summary>
    public async Task<bool> HasSuccessMessageAsync(string expectedText)
    {
        var locator = page.Locator(".alert-success");
        await locator.WaitForAsync(new() { Timeout = 10_000 });
        var text = await locator.InnerTextAsync();
        return text.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns true if the given upstream ID appears as a row in the "New On Nodes" table.</summary>
    public async Task<bool> HasNewOnNodesRowAsync(string upstreamId)
    {
        return await page
            .Locator($"[data-testid='models-sync-row'][data-upstream-id='{upstreamId}']")
            .CountAsync() > 0;
    }
}
