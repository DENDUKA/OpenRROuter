using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class InMemoryAuthRepository : IAuthRepository
{
    private readonly object gate = new();
    private readonly Dictionary<string, UserAccount> users = [];
    private readonly List<Invite> invites = [];
    private readonly List<ApiKey> apiKeys = [];

    public void AddInvite(Invite invite)
    {
        lock (gate)
        {
            invites.Add(invite);
        }
    }

    public Invite? GetInviteByTokenHash(string tokenHash)
    {
        lock (gate)
        {
            return invites.SingleOrDefault(invite => invite.TokenHash == tokenHash);
        }
    }

    public void AddUserAndMarkInviteUsed(UserAccount user, Invite invite)
    {
        lock (gate)
        {
            users[user.Id] = user;
        }
    }

    public UserAccount? GetUser(string userId)
    {
        lock (gate)
        {
            return users.GetValueOrDefault(userId);
        }
    }

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

    public void AddApiKey(ApiKey apiKey)
    {
        lock (gate)
        {
            apiKeys.Add(apiKey);
        }
    }

    public ApiKey? GetApiKey(string apiKeyId)
    {
        lock (gate)
        {
            return apiKeys.SingleOrDefault(key => key.Id == apiKeyId);
        }
    }

    public ApiKey? GetApiKeyBySecretHash(string secretHash)
    {
        lock (gate)
        {
            return apiKeys.SingleOrDefault(key => key.SecretHash == secretHash);
        }
    }

    public IReadOnlyCollection<ApiKey> ListApiKeys(string userId)
    {
        lock (gate)
        {
            return apiKeys.Where(key => key.UserId == userId).ToArray();
        }
    }

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

    public IReadOnlyCollection<(string Prefix, string SecretHash)> ListActiveApiKeyHashes()
    {
        lock (gate)
        {
            return apiKeys
                .Where(k => k.RevokedAtUtc is null)
                .Select(k => (k.Prefix, k.SecretHash))
                .ToArray();
        }
    }

    public string? GetFirstActiveApiKeyRaw(string userId)
    {
        lock (gate)
        {
            return apiKeys
                .Where(k => k.UserId == userId && k.RevokedAtUtc is null && k.RawKey is not null)
                .OrderByDescending(k => k.CreatedAtUtc)
                .Select(k => k.RawKey)
                .FirstOrDefault();
        }
    }
}
