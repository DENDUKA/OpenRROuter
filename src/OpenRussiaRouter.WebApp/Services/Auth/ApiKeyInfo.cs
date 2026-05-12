namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record ApiKeyInfo(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsed, bool IsActive);