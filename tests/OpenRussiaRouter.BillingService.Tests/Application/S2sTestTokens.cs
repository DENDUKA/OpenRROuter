namespace OpenRussiaRouter.BillingService.Tests.Application;

/// <summary>
/// Shared test fixtures for s2s auth tests. The integration tests authoritatively replace
/// <c>ServiceAuthOptions</c> in DI via <c>PostConfigure</c>, so these values do NOT have to
/// match the tokens seeded in <c>BillingService/appsettings*.json</c> — they only have to
/// match what the test host installs.
/// </summary>
internal static class S2sTestTokens
{
    public const string ApiGateway = "dev-s2s-token-gateway-CHANGE-ME";
    public const string WebappAdmin = "dev-s2s-token-webapp-CHANGE-ME";
}
