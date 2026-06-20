namespace OpenRussiaRouter.ApiGateway.Router;

public sealed record RouterProxyResponse(
    int StatusCode,
    string ContentType,
    string Body,
    string? ProviderName = null,
    string? ProviderAccount = null,
    byte[]? BodyBytes = null);
