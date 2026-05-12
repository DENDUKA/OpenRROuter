using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class AuthAccountApplicationService(
    IAuthRepository repository,
    InviteRegistrationService inviteRegistration,
    ApiKeyFactory apiKeyFactory,
    Sha256SecretHasher secretHasher)
{

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

    public UserAccount CompleteInvite(string rawToken, string password, DateTimeOffset nowUtc)
    {
        var tokenHash = secretHasher.Hash(rawToken);
        var invite = repository.GetInviteByTokenHash(tokenHash)
            ?? throw new InvalidOperationException("Invite token is invalid.");
        var user = inviteRegistration.CompleteInvite(invite, rawToken, password, nowUtc);
        repository.AddUserAndMarkInviteUsed(user, invite);
        return user;
    }

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

    public void RenameApiKey(string userId, string apiKeyId, string name)
    {
        var probe = new ApiKey(apiKeyId, userId, name, "prefix", "hash", DateTimeOffset.UtcNow);
        if (!repository.RenameApiKey(probe.UserId, probe.Id, probe.Name))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    public void RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc)
    {
        if (!repository.RevokeApiKey(RequireValue(userId, nameof(userId)), RequireValue(apiKeyId, nameof(apiKeyId)), revokedAtUtc))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    public IReadOnlyCollection<ApiKey> ListApiKeys(string userId)
    {
        return repository.ListApiKeys(RequireValue(userId, nameof(userId)));
    }

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
