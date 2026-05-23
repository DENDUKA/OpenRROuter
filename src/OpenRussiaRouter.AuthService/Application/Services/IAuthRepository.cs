using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

public interface IAuthRepository
{
    Task AddInviteAsync(Invite invite, CancellationToken ct = default);

    Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddUserAndMarkInviteUsedAsync(UserAccount user, Invite invite, CancellationToken ct = default);

    Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default);

    Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default);

    Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, CancellationToken ct = default);

    Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default);

    Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, CancellationToken ct = default);

    Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default);

    Task<bool> RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default);

    Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default);

    Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default);

    Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default);

    Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default);
}
