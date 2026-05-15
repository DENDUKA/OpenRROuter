namespace OpenRussiaRouter.WebApp.Services.State;

public sealed class UserSession
{
    public required string SessionToken { get; init; }
    public required string UserId { get; init; }
    public required string Role { get; init; }
    public string Email { get; init; } = "";
    public bool IsAdmin => Role == "Admin";
    public string DisplayName => Email.Contains('@') ? Email[..Email.IndexOf('@')] : (Email.Length > 0 ? Email : UserId);
}