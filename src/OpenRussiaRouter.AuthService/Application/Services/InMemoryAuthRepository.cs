using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class InMemoryAuthRepository : IAuthRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UserAccount> _users = [];
    private readonly List<Invite> _invites = [];
    private readonly List<ApiKey> _apiKeys = [];
    private readonly List<ExternalLogin> _externalLogins = [];

    public Task AddInviteAsync(Invite invite, CancellationToken ct = default)
    {
        lock (_gate) { _invites.Add(invite); }
        return Task.CompletedTask;
    }

    public Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_invites.SingleOrDefault(i => i.TokenHash == tokenHash));
        }
    }

    public Task AddUserAndMarkInviteUsedAsync(UserAccount user, Invite invite, CancellationToken ct = default)
    {
        lock (_gate) { _users[user.Id] = user; }
        return Task.CompletedTask;
    }

    public Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_users.GetValueOrDefault(userId)); }
    }

    public Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_users.Values.SingleOrDefault(u => u.Email == email.ToLowerInvariant()));
        }
    }

    public Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var all = _users.Values.OrderByDescending(u => u.CreatedAtUtc).Take(limit + 1).ToArray();
            var hasMore = all.Length > limit;
            IReadOnlyCollection<UserAccount> result = hasMore ? all.Take(limit).ToArray() : all;
            return Task.FromResult((result, hasMore));
        }
    }

    public Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default)
    {
        lock (_gate) { _apiKeys.Add(apiKey); }
        return Task.CompletedTask;
    }

    public Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_apiKeys.SingleOrDefault(k => k.Id == apiKeyId)); }
    }

    public Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var all = _apiKeys
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAtUtc)
                .Take(limit + 1)
                .ToArray();
            var hasMore = all.Length > limit;
            IReadOnlyCollection<ApiKey> result = hasMore ? all.Take(limit).ToArray() : all;
            return Task.FromResult((result, hasMore));
        }
    }

    public Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var key = _apiKeys.SingleOrDefault(k => k.UserId == userId && k.Id == apiKeyId);
            if (key is null) return Task.FromResult(false);
            key.Rename(name);
            return Task.FromResult(true);
        }
    }

    public Task<bool> RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var key = _apiKeys.SingleOrDefault(k => k.UserId == userId && k.Id == apiKeyId);
            if (key is null) return Task.FromResult(false);
            key.Revoke(revokedAtUtc);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyCollection<(string, string)> result = _apiKeys
                .Where(k => k.RevokedAtUtc is null)
                .Select(k => (k.Prefix, k.SecretHash))
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var raw = _apiKeys
                .Where(k => k.UserId == userId && k.RevokedAtUtc is null && k.RawKey is not null)
                .OrderByDescending(k => k.CreatedAtUtc)
                .Select(k => k.RawKey)
                .FirstOrDefault();
            return Task.FromResult(raw);
        }
    }

    public Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_apiKeys.SingleOrDefault(k => k.SecretHash == secretHash && k.RevokedAtUtc is null));
        }
    }

    public Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var key = _apiKeys.SingleOrDefault(k => k.Id == apiKeyId);
            if (key is { IsActive: true }) key.MarkUsed(lastUsedAtUtc);
        }
        return Task.CompletedTask;
    }

    public Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_externalLogins.SingleOrDefault(l => l.Provider == provider && l.ProviderSub == providerSub));
        }
    }

    public Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
    {
        lock (_gate) { _externalLogins.Add(login); }
        return Task.CompletedTask;
    }

    public Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _users[user.Id] = user;
            _externalLogins.Add(login);
        }
        return Task.CompletedTask;
    }
}
