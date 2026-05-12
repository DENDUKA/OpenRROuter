namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record InviteCompletionResponse(string UserId, string Email, string Role);