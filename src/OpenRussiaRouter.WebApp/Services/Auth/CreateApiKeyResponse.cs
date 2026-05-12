namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record CreateApiKeyResponse(string ApiKeyId, string RawKey, string Name);