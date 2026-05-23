namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class ExternalLoginEntity
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderSub { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
