using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// Coordinates persisted Auth workflows for invites, users and API keys.
/// </summary>
public sealed class AuthAccountApplicationService(
    IAuthRepository repository,
    InviteRegistrationService inviteRegistration,
    ApiKeyFactory apiKeyFactory,
    Sha256SecretHasher secretHasher)
{
    /// <summary>
    /// Creates and stores an admin invite.
    /// </summary>
    /// <param name="email">Invited email.</param>
    /// <param name="role">Role assigned to the created user.</param>
    /// <param name="createdByAdminId">Admin user id.</param>
    /// <param name="expiresAtUtc">Expiration timestamp.</param>
    /// <param name="nowUtc">Creation timestamp.</param>
    /// <returns>Invite plus raw token shown once.</returns>
    public CreatedInvite CreateInvite(
        string email,
        UserRole role,
        string createdByAdminId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        var created = inviteRegistration.CreateInvite(email, role, createdByAdminId, expiresAtUtc, nowUtc);
        repository.AddInvite(created.Invite);
        return created;
    }

    /// <summary>
    /// Completes an invite, stores the user and marks the invite as used.
    /// </summary>
    /// <param name="rawToken">Raw invite token.</param>
    /// <param name="password">Raw password.</param>
    /// <param name="nowUtc">Completion timestamp.</param>
    /// <returns>Created user account.</returns>
    public UserAccount CompleteInvite(string rawToken, string password, DateTimeOffset nowUtc)
    {
        var tokenHash = secretHasher.Hash(rawToken);
        var invite = repository.GetInviteByTokenHash(tokenHash)
            ?? throw new InvalidOperationException("Invite token is invalid.");
        var user = inviteRegistration.CompleteInvite(invite, rawToken, password, nowUtc);
        repository.AddUserAndMarkInviteUsed(user, invite);
        return user;
    }

    /// <summary>
    /// Creates and stores a user API key.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="name">User-visible key name.</param>
    /// <param name="nowUtc">Creation timestamp.</param>
    /// <returns>API key plus raw key shown once.</returns>
    public CreatedApiKey CreateApiKey(string userId, string name, DateTimeOffset nowUtc)
    {
        if (repository.GetUser(userId) is not { Status: UserStatus.Active })
        {
            throw new InvalidOperationException("User is not active.");
        }

        var created = apiKeyFactory.Create(userId, name, nowUtc);
        repository.AddApiKey(created.ApiKey);
        return created;
    }

    /// <summary>
    /// Renames a user API key.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="apiKeyId">API key id.</param>
    /// <param name="name">New display name.</param>
    public void RenameApiKey(string userId, string apiKeyId, string name)
    {
        var probe = new ApiKey(apiKeyId, userId, name, "prefix", "hash", DateTimeOffset.UtcNow);
        if (!repository.RenameApiKey(probe.UserId, probe.Id, probe.Name))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    /// <summary>
    /// Revokes a user API key.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="apiKeyId">API key id.</param>
    /// <param name="revokedAtUtc">Revocation timestamp.</param>
    public void RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc)
    {
        if (!repository.RevokeApiKey(RequireValue(userId, nameof(userId)), RequireValue(apiKeyId, nameof(apiKeyId)), revokedAtUtc))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    /// <summary>
    /// Lists user API keys without raw secrets.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <returns>API keys.</returns>
    public IReadOnlyCollection<ApiKey> ListApiKeys(string userId)
    {
        return repository.ListApiKeys(RequireValue(userId, nameof(userId)));
    }

    /// <summary>
    /// Lists all users for admin views.
    /// </summary>
    /// <returns>All user accounts.</returns>
    public IReadOnlyCollection<UserAccount> ListUsers()
    {
        return repository.ListUsers();
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }

        return value.Trim();
    }
}
