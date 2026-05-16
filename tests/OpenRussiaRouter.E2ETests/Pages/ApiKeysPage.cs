using Microsoft.Playwright;

namespace OpenRussiaRouter.E2ETests.Pages;

sealed class ApiKeysPage(IPage page, string baseUrl)
{
    public async Task NavigateAsync()
    {
        await page.GotoAsync($"{baseUrl}/workspace/apikeys");
        await page.WaitForSelectorAsync("[data-testid='btn-create-key']");
    }

    public async Task<string> CreateKeyAsync(string name)
    {
        await page.ClickAsync("[data-testid='btn-create-key']");
        await page.WaitForSelectorAsync("[data-testid='input-key-name']");
        await page.FillAsync("[data-testid='input-key-name']", name);
        await page.ClickAsync("[data-testid='btn-submit-create']");

        await page.WaitForSelectorAsync("[data-testid='new-key-value']");
        var rawKey = await page.TextContentAsync("[data-testid='new-key-value']") ?? string.Empty;

        await page.ClickAsync("[data-testid='btn-done-create']");
        await page.WaitForSelectorAsync($"[data-key-name='{name}']");

        return rawKey.Trim();
    }

    public async Task RenameKeyAsync(string currentName, string newName)
    {
        var row = page.Locator($"[data-testid='key-row'][data-key-name='{currentName}']");
        await row.Locator("[data-testid='btn-rename']").ClickAsync();

        var input = page.Locator("[data-testid='input-rename']");
        await input.WaitForAsync();
        await input.ClearAsync();
        await input.FillAsync(newName);

        await page.ClickAsync("[data-testid='btn-save-rename']");
        await page.WaitForSelectorAsync($"[data-key-name='{newName}']");
    }

    public async Task RevokeKeyAsync(string name)
    {
        var row = page.Locator($"[data-testid='key-row'][data-key-name='{name}']");
        await row.Locator("[data-testid='btn-revoke']").ClickAsync();

        await page.WaitForSelectorAsync("[data-testid='btn-confirm-revoke']");
        await page.ClickAsync("[data-testid='btn-confirm-revoke']");

        await page.WaitForSelectorAsync("[data-testid='btn-confirm-revoke']",
            new() { State = WaitForSelectorState.Hidden });
    }

    public async Task<string> GetKeyStatusAsync(string name)
    {
        var row = page.Locator($"[data-testid='key-row'][data-key-name='{name}']");
        return (await row.Locator("[data-testid='key-status']").TextContentAsync() ?? string.Empty).Trim();
    }

    public ILocator GetKeyRow(string name) =>
        page.Locator($"[data-testid='key-row'][data-key-name='{name}']");
}
