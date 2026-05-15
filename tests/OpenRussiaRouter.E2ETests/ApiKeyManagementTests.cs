using OpenRussiaRouter.E2ETests.Pages;

namespace OpenRussiaRouter.E2ETests;

/// <summary>
/// Full browser E2E flow: login → create key → rename → revoke.
///
/// Required env vars:
///   ORR_E2E_USER_EMAIL    — test user email
///   ORR_E2E_USER_PASSWORD — test user password
///   ORR_E2E_BASE_URL      — WebApp URL (default: http://localhost:5004)
///
/// Before first run, install browsers:
///   pwsh bin/Debug/net8.0/playwright.ps1 install chromium
/// </summary>
public sealed class ApiKeyManagementTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public ApiKeyManagementTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApiKeyLifecycle_CreateRenameRevoke_FullFlow()
    {
        if (!TestConfiguration.IsConfigured)
        {
            // Skip gracefully when credentials are not provided
            return;
        }

        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var baseUrl = TestConfiguration.BaseUrl;

        // Step 1: Login
        var loginPage = new LoginPage(page, baseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(TestConfiguration.UserEmail!, TestConfiguration.UserPassword!);

        // Step 2: Navigate to API Keys
        var keysPage = new ApiKeysPage(page, baseUrl);
        await keysPage.NavigateAsync();

        // Step 3: Create a key — raw key must be shown once
        var rawKey = await keysPage.CreateKeyAsync("E2E Test Key");
        Assert.NotEmpty(rawKey);
        Assert.StartsWith("orr_", rawKey);

        // Step 4: Key appears in list as Active
        var statusAfterCreate = await keysPage.GetKeyStatusAsync("E2E Test Key");
        Assert.Equal("Active", statusAfterCreate);

        // Step 5: Rename — new name appears, old name gone
        await keysPage.RenameKeyAsync("E2E Test Key", "E2E Renamed Key");
        var statusAfterRename = await keysPage.GetKeyStatusAsync("E2E Renamed Key");
        Assert.Equal("Active", statusAfterRename);
        Assert.Equal(0, await keysPage.GetKeyRow("E2E Test Key").CountAsync());

        // Step 6: Revoke — status changes to Revoked
        await keysPage.RevokeKeyAsync("E2E Renamed Key");
        var statusAfterRevoke = await keysPage.GetKeyStatusAsync("E2E Renamed Key");
        Assert.Equal("Revoked", statusAfterRevoke);

        // Step 7: Action buttons disappear for revoked key
        var revokedRow = keysPage.GetKeyRow("E2E Renamed Key");
        Assert.Equal(0, await revokedRow.Locator("[data-testid='btn-rename']").CountAsync());
        Assert.Equal(0, await revokedRow.Locator("[data-testid='btn-revoke']").CountAsync());
    }
}
