namespace OpenRussiaRouter.E2ETests.Pages;

sealed class LoginPage(IPage page, string baseUrl)
{
    public async Task NavigateAsync()
    {
        await page.GotoAsync($"{baseUrl}/login");
        await page.WaitForSelectorAsync("#Input_Email");
    }

    public async Task LoginAsync(string email, string password)
    {
        await page.FillAsync("#Input_Email", email);
        await page.FillAsync("#Input_Password", password);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 10_000 });
    }
}
