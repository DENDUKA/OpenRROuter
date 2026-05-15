namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record LoginResponse(string SessionToken, string UserId, string Role, string Email);