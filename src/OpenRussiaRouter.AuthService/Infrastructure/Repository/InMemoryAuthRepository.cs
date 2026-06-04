using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Infrastructure.Repository;

public sealed class InMemoryAuthRepository : IAuthRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UserAccount> _users = [];
    private readonly List<Invite> _invites = [];
    private readonly List<ApiKey> _apiKeys = [];
    private readonly List<ExternalLogin> _externalLogins = [];
    private readonly Dictionary<string, EmailVerification> _emailVerifications = [];

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

    public Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, string? afterUserId = null, CancellationToken ct = default)
    {
        var query = _users.Values.OrderByDescending(u => u.CreatedAtUtc).AsEnumerable();
        
        if (afterUserId != null)
        {
            var user = _users.GetValueOrDefault(afterUserId);
            if (user != null)
            {
                query = query.Where(u => u.CreatedAtUtc < user.CreatedAtUtc || (u.CreatedAtUtc == user.CreatedAtUtc && string.Compare(u.Id, user.Id, StringComparison.Ordinal) < 0));
            }
        }

        var items = query.Take(limit + 1).ToList();
        var hasMore = items.Count > limit;
        if (hasMore) items = items.Take(limit).ToList();
        return Task.FromResult(((IReadOnlyCollection<UserAccount>)items, hasMore));
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

    public Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, string? afterApiKeyId = null, CancellationToken ct = default)
    {
        var query = _apiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .AsEnumerable();
            
        if (afterApiKeyId != null)
        {
            var key = _apiKeys.FirstOrDefault(k => k.Id == afterApiKeyId);
            if (key != null)
            {
                query = query.Where(k => k.CreatedAtUtc < key.CreatedAtUtc || (k.CreatedAtUtc == key.CreatedAtUtc && string.Compare(k.Id, key.Id, StringComparison.Ordinal) < 0));
            }
        }
            
        var items = query.Take(limit + 1).ToList();
        var hasMore = items.Count > limit;
        if (hasMore) items = items.Take(limit).ToList();
        return Task.FromResult(((IReadOnlyCollection<ApiKey>)items, hasMore));
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

    public Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name)>> ListAllActiveApiKeysRawAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyCollection<(string, string, string, string)> result = _apiKeys
                .Where(k => k.RevokedAtUtc is null && k.RawKey is not null)
                .Select(k => (k.Id, k.RawKey!, k.UserId, k.Name))
                .ToArray();
            return Task.FromResult(result);
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

    public Task AddUserWithEmailVerificationAsync(UserAccount user, EmailVerification verification, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _users[user.Id] = user;
            _emailVerifications[verification.UserId] = verification;
        }
        return Task.CompletedTask;
    }

    public Task<EmailVerification?> GetEmailVerificationAsync(string userId, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_emailVerifications.GetValueOrDefault(userId)); }
    }

    public Task UpdateEmailVerificationAsync(EmailVerification verification, CancellationToken ct = default)
    {
        lock (_gate) { _emailVerifications[verification.UserId] = verification; }
        return Task.CompletedTask;
    }

    public Task ActivateUserAndDeleteVerificationAsync(string userId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_users.TryGetValue(userId, out var user)) user.Activate();
            _emailVerifications.Remove(userId);
        }
        return Task.CompletedTask;
    }
}
