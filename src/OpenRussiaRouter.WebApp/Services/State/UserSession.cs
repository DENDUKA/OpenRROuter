namespace OpenRussiaRouter.WebApp.Services.State;

public sealed class UserSession
{
    public required string SessionToken { get; init; }
    public required string UserId { get; init; }
    public required string Role { get; init; }
    public bool IsAdmin => Role == "Admin";
}