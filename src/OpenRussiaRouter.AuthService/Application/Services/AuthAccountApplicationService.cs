using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class AuthAccountApplicationService(
    IAuthRepository repository,
    InviteRegistrationService inviteRegistration,
    ApiKeyFactory apiKeyFactory,
    Sha256SecretHasher secretHasher)
{
    private const int DefaultPageSize = 1000;

    public async Task<CreatedInvite> CreateInviteAsync(
        string email,
        UserRole role,
        string createdByAdminId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var created = inviteRegistration.CreateInvite(email, role, createdByAdminId, expiresAtUtc, nowUtc);
        await repository.AddInviteAsync(created.Invite, ct).ConfigureAwait(false);
        return created;
    }

    public async Task<UserAccount> CompleteInviteAsync(string rawToken, string password, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var tokenHash = secretHasher.Hash(rawToken);
        var invite = await repository.GetInviteByTokenHashAsync(tokenHash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Invite token is invalid.");
        var user = inviteRegistration.CompleteInvite(invite, rawToken, password, nowUtc);
        await repository.AddUserAndMarkInviteUsedAsync(user, invite, ct).ConfigureAwait(false);
        return user;
    }

    public async Task<CreatedApiKey> CreateApiKeyAsync(string userId, string name, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (await repository.GetUserAsync(userId, ct).ConfigureAwait(false) is not { Status: UserStatus.Active })
            throw new InvalidOperationException("User is not active.");

        var created = apiKeyFactory.Create(userId, name, nowUtc);
        await repository.AddApiKeyAsync(created.ApiKey, ct).ConfigureAwait(false);
        return created;
    }

    public async Task RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default)
    {
        if (!await repository.RenameApiKeyAsync(
                Preconditions.RequireValue(userId, nameof(userId)),
                Preconditions.RequireValue(apiKeyId, nameof(apiKeyId)),
                Preconditions.RequireValue(name, nameof(name)),
                ct).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    public async Task RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default)
    {
        if (!await repository.RevokeApiKeyAsync(
                Preconditions.RequireValue(userId, nameof(userId)),
                Preconditions.RequireValue(apiKeyId, nameof(apiKeyId)),
                revokedAtUtc,
                ct).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("API key was not found.");
        }
    }

    public async Task<IReadOnlyCollection<ApiKey>> ListApiKeysAsync(string userId, CancellationToken ct = default)
    {
        return (await repository.ListApiKeysAsync(
            Preconditions.RequireValue(userId, nameof(userId)),
            DefaultPageSize,
            null,
            ct).ConfigureAwait(false)).Items;
    }

    public async Task<IReadOnlyCollection<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        return (await repository.ListUsersAsync(DefaultPageSize, null, ct).ConfigureAwait(false)).Items;
    }
}
