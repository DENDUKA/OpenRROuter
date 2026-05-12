using OpenRussiaRouter.AuthService.Domain;

namespace OpenRussiaRouter.AuthService.Application;

/// <summary>
/// Persistence operations for Auth user, invite and API key lifecycle.
/// </summary>
public interface IAuthRepository
{
    /// <summary>
    /// Adds a new invite.
    /// </summary>
    /// <param name="invite">Invite to persist.</param>
    void AddInvite(Invite invite);

    /// <summary>
    /// Gets an invite by token hash.
    /// </summary>
    /// <param name="tokenHash">Invite token hash.</param>
    /// <returns>Invite or null.</returns>
    Invite? GetInviteByTokenHash(string tokenHash);

    /// <summary>
    /// Adds a user and marks the invite as used atomically.
    /// </summary>
    /// <param name="user">Created user.</param>
    /// <param name="invite">Used invite.</param>
    void AddUserAndMarkInviteUsed(UserAccount user, Invite invite);

    /// <summary>
    /// Gets a user by id.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <returns>User or null.</returns>
    UserAccount? GetUser(string userId);

    /// <summary>
    /// Adds a new API key.
    /// </summary>
    /// <param name="apiKey">API key metadata and secret hash.</param>
    void AddApiKey(ApiKey apiKey);

    /// <summary>
    /// Gets an API key by id.
    /// </summary>
    /// <param name="apiKeyId">API key id.</param>
    /// <returns>API key or null.</returns>
    ApiKey? GetApiKey(string apiKeyId);

    /// <summary>
    /// Lists API keys for a user.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <returns>User API keys.</returns>
    IReadOnlyCollection<ApiKey> ListApiKeys(string userId);

    /// <summary>
    /// Renames an API key owned by the user.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="apiKeyId">API key id.</param>
    /// <param name="name">New name.</param>
    /// <returns>Whether a key was updated.</returns>
    bool RenameApiKey(string userId, string apiKeyId, string name);

    /// <summary>
    /// Revokes an API key owned by the user.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="apiKeyId">API key id.</param>
    /// <param name="revokedAtUtc">Revocation timestamp.</param>
    /// <returns>Whether a key was updated.</returns>
    bool RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc);
}
