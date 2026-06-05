using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

public interface IAuthRepository
{
    Task AddInviteAsync(Invite invite, CancellationToken ct = default);

    Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddUserAndMarkInviteUsedAsync(UserAccount user, Invite invite, CancellationToken ct = default);

    Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default);

    Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default);

    Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, string? afterUserId = null, CancellationToken ct = default);

    Task UpdateUserAsync(UserAccount user, CancellationToken ct = default);

    Task<int> CountApiKeysAsync(string userId, CancellationToken ct = default);

    Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default);

    Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default);

    Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, string? afterApiKeyId = null, bool includeDeleted = false, CancellationToken ct = default);

    Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default);

    Task<bool> RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default);

    Task<bool> DeleteApiKeyAsync(string userId, string apiKeyId, DateTimeOffset deletedAtUtc, CancellationToken ct = default);

    Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default);

    Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name)>> ListAllActiveApiKeysRawAsync(CancellationToken ct = default);

    Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default);

    Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default);

    Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default);

    Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default);

    Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default);

    Task AddUserWithEmailVerificationAsync(UserAccount user, EmailVerification verification, CancellationToken ct = default);

    Task<EmailVerification?> GetEmailVerificationAsync(string userId, CancellationToken ct = default);

    Task UpdateEmailVerificationAsync(EmailVerification verification, CancellationToken ct = default);

    Task ActivateUserAndDeleteVerificationAsync(string userId, CancellationToken ct = default);
}
