namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record UserInfo(string Id, string Email, string Role, string Status, DateTimeOffset CreatedAt);