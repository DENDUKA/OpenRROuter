namespace OpenRussiaRouter.BillingService.Tests.Application;

/// <summary>
/// Shared test fixtures for s2s auth tests. Values must match the dev tokens configured in
/// <c>BillingService/appsettings.json</c> and <c>appsettings.Development.json</c>.
/// </summary>
internal static class S2sTestTokens
{
    public const string ApiGateway = "dev-s2s-token-gateway-CHANGE-ME";
    public const string WebappAdmin = "dev-s2s-token-webapp-CHANGE-ME";
}
