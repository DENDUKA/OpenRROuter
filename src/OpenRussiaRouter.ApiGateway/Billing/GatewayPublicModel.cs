namespace OpenRussiaRouter.ApiGateway.Billing;

/// <summary>
/// Public model exposed by Gateway.
/// </summary>
/// <param name="Id">Public model id.</param>
/// <param name="DisplayName">Display name.</param>
public sealed record GatewayPublicModel(string Id, string DisplayName);

