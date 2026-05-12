using OpenRussiaRouter.AuthService.Application.Interfaces;
using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// In-memory Auth repository for local development and unit tests.
/// </summary>
public sealed class InMemoryAuthRepository : IAuthRepository
{
    private readonly object gate = new();
    private readonly Dictionary<string, UserAccount> users = [];
    private readonly List<Invite> invites = [];
    private readonly List<ApiKey> apiKeys = [];

    /// <inheritdoc />
    public void AddInvite(Invite invite)
    {
        lock (gate)
        {
            invites.Add(invite);
        }
    }

    /// <inheritdoc />
    public Invite? GetInviteByTokenHash(string tokenHash)
    {
        lock (gate)
        {
            return invites.SingleOrDefault(invite => invite.TokenHash == tokenHash);
        }
    }

    /// <inheritdoc />
    public void AddUserAndMarkInviteUsed(UserAccount user, Invite invite)
    {
        lock (gate)
        {
            users[user.Id] = user;
        }
    }

    /// <inheritdoc />
    public UserAccount? GetUser(string userId)
    {
        lock (gate)
        {
            return users.GetValueOrDefault(userId);
        }
    }

    /// <inheritdoc />
    public UserAccount? GetUserByEmail(string email)
    {
        lock (gate)
        {
            return users.Values.SingleOrDefault(u => u.Email == email.ToLowerInvariant());
        }
    }

    public IReadOnlyCollection<UserAccount> ListUsers()
    {
        lock (gate)
        {
            return users.Values.ToArray();
        }
    }

    /// <inheritdoc />
    public void AddApiKey(ApiKey apiKey)
    {
        lock (gate)
        {
            apiKeys.Add(apiKey);
        }
    }

    /// <inheritdoc />
    public ApiKey? GetApiKey(string apiKeyId)
    {
        lock (gate)
        {
            return apiKeys.SingleOrDefault(key => key.Id == apiKeyId);
        }
    }

    /// <summary>
    /// Gets an API key by stored secret hash.
    /// </summary>
    /// <param name="secretHash">Stored secret hash.</param>
    /// <returns>API key or null.</returns>
    public ApiKey? GetApiKeyBySecretHash(string secretHash)
    {
        lock (gate)
        {
            return apiKeys.SingleOrDefault(key => key.SecretHash == secretHash);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ApiKey> ListApiKeys(string userId)
    {
        lock (gate)
        {
            return apiKeys.Where(key => key.UserId == userId).ToArray();
        }
    }

    /// <inheritdoc />
    public bool RenameApiKey(string userId, string apiKeyId, string name)
    {
        lock (gate)
        {
            var key = apiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null)
            {
                return false;
            }

            key.Rename(name);
            return true;
        }
    }

    /// <inheritdoc />
    public bool RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc)
    {
        lock (gate)
        {
            var key = apiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null)
            {
                return false;
            }

            key.Revoke(revokedAtUtc);
            return true;
        }
    }
}
