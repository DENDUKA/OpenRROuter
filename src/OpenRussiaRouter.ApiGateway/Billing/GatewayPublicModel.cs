namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed record GatewayPublicModel(
    string Id,
    string DisplayName,
    bool SupportsReasoning = false,
    IReadOnlyList<string>? AllowedReasoningEfforts = null);

