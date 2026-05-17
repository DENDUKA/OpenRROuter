using System.Globalization;

namespace OpenRussiaRouter.E2ETests.Pages;

sealed class ModelsAdminPage(IPage page, string baseUrl)
{
    private readonly string _listUrl = $"{baseUrl.TrimEnd('/')}/admin/models";

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
        await page.FillAsync("#Form_InputPrice",  inputPrice.ToString("F2", CultureInfo.InvariantCulture));
        await page.FillAsync("#Form_OutputPrice", outputPrice.ToString("F2", CultureInfo.InvariantCulture));

        await page.ClickAsync("button.btn-primary");

        // After save the handler does RedirectToPage() → URL drops ?action=add
        await page.WaitForURLAsync(
            url => !url.Contains("action="),
            new() { Timeout = 15_000 });

        // The model ID should now appear as a <code> cell in the list table
        await page.WaitForSelectorAsync($"code:text-is('{id}')");
    }
}
