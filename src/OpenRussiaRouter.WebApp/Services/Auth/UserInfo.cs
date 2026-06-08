namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record UserInfo(string Id, string Email, string Role, string Status, DateTimeOffset CreatedAt, string? DisplayName, int MaxApiKeys, string DisplayCurrency = "RUB");