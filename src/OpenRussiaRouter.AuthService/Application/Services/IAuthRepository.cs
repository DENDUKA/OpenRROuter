using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

public interface IAuthRepository
{

    void AddInvite(Invite invite);

    Invite? GetInviteByTokenHash(string tokenHash);

    void AddUserAndMarkInviteUsed(UserAccount user, Invite invite);

UserAccount? GetUser(string userId);

    UserAccount? GetUserByEmail(string email);

    IReadOnlyCollection<UserAccount> ListUsers();

    void AddApiKey(ApiKey apiKey);

    ApiKey? GetApiKey(string apiKeyId);

    IReadOnlyCollection<ApiKey> ListApiKeys(string userId);

    bool RenameApiKey(string userId, string apiKeyId, string name);

    bool RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc);

    IReadOnlyCollection<(string Prefix, string SecretHash)> ListActiveApiKeyHashes();

    string? GetFirstActiveApiKeyRaw(string userId);

    ApiKey? FindActiveApiKeyBySecretHash(string secretHash);

    void MarkApiKeyUsed(string apiKeyId, DateTimeOffset lastUsedAtUtc);
}