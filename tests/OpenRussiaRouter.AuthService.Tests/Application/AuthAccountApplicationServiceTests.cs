using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class AuthAccountApplicationServiceTests
{
    [Fact]
    public void CreateInvite_StoresInviteAndReturnsRawTokenOnce()
    {
        var repository = new TestAuthRepository();
        var service = CreateService(repository);
        var now = DateTimeOffset.UtcNow;

        var created = service.CreateInvite("USER@example.com", UserRole.User, "admin-1", now.AddDays(1), now);

        Assert.StartsWith("orr_invite_", created.RawToken, StringComparison.Ordinal);
        Assert.Single(repository.Invites);
        Assert.NotEqual(created.RawToken, repository.Invites.Single().TokenHash);
        Assert.Equal("user@example.com", repository.Invites.Single().Email);
    }

    [Fact]
    public void CompleteInvite_ValidToken_StoresUserAndMarksInviteUsed()
    {
        var repository = new TestAuthRepository();
        var service = CreateService(repository);
        var now = DateTimeOffset.UtcNow;
        var created = service.CreateInvite("user@example.com", UserRole.Admin, "admin-1", now.AddDays(1), now);

        var user = service.CompleteInvite(created.RawToken, "StrongPassword123!", now.AddMinutes(1));

        Assert.Equal("user@example.com", user.Email);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Single(repository.Users);
        Assert.True(repository.Invites.Single().IsUsed);
    }

    [Fact]
    public void CreateApiKey_ActiveUser_StoresHashAndReturnsRawKey()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;
        var service = CreateService(repository);

        var created = service.CreateApiKey("user-1", "default", DateTimeOffset.UtcNow);

        Assert.StartsWith("orr_", created.RawKey, StringComparison.Ordinal);
        Assert.Single(repository.ApiKeys);
        Assert.Equal(created.ApiKey.SecretHash, repository.ApiKeys.Single().SecretHash);
        Assert.NotEqual(created.RawKey, repository.ApiKeys.Single().SecretHash);
    }

    [Fact]
    public void CreateApiKey_DisabledUser_Throws()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        user.Disable();
        repository.Users[user.Id] = user;
        var service = CreateService(repository);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateApiKey("user-1", "default", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RenameAndRevokeApiKey_ExistingKey_UpdatesMetadata()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;
        var service = CreateService(repository);
        var created = service.CreateApiKey("user-1", "default", DateTimeOffset.UtcNow);
        var revokedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        service.RenameApiKey("user-1", created.ApiKey.Id, "renamed");
        service.RevokeApiKey("user-1", created.ApiKey.Id, revokedAt);

        var key = Assert.Single(repository.ApiKeys);
        Assert.Equal("renamed", key.Name);
        Assert.Equal(revokedAt, key.RevokedAtUtc);
    }

    private static AuthAccountApplicationService CreateService(TestAuthRepository repository)
    {
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        return new AuthAccountApplicationService(
            repository,
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
    }

    private sealed class TestAuthRepository : IAuthRepository
    {
        public Dictionary<string, UserAccount> Users { get; } = new(StringComparer.Ordinal);

        public List<Invite> Invites { get; } = [];

        public List<ApiKey> ApiKeys { get; } = [];

        public void AddInvite(Invite invite)
        {
            Invites.Add(invite);
        }

        public Invite? GetInviteByTokenHash(string tokenHash)
        {
            return Invites.SingleOrDefault(invite => invite.TokenHash == tokenHash);
        }

        public void AddUserAndMarkInviteUsed(UserAccount user, Invite invite)
        {
            Users[user.Id] = user;
        }

        public UserAccount? GetUser(string userId)
        {
            return Users.GetValueOrDefault(userId);
        }

        public UserAccount? GetUserByEmail(string email)
        {
            return Users.Values.SingleOrDefault(u => u.Email == email.ToLowerInvariant());
        }

        public IReadOnlyCollection<UserAccount> ListUsers()
        {
            return Users.Values.ToArray();
        }

        public void AddApiKey(ApiKey apiKey)
        {
            ApiKeys.Add(apiKey);
        }

        public ApiKey? GetApiKey(string apiKeyId)
        {
            return ApiKeys.SingleOrDefault(key => key.Id == apiKeyId);
        }

        public IReadOnlyCollection<ApiKey> ListApiKeys(string userId)
        {
            return ApiKeys.Where(key => key.UserId == userId).ToArray();
        }

        public bool RenameApiKey(string userId, string apiKeyId, string name)
        {
            var key = ApiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null)
            {
                return false;
            }

            key.Rename(name);
            return true;
        }

        public bool RevokeApiKey(string userId, string apiKeyId, DateTimeOffset revokedAtUtc)
        {
            var key = ApiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null)
            {
                return false;
            }

            key.Revoke(revokedAtUtc);
            return true;
        }

        public IReadOnlyCollection<(string Prefix, string SecretHash)> ListActiveApiKeyHashes()
        {
            return ApiKeys
                .Where(k => k.RevokedAtUtc is null)
                .Select(k => (k.Prefix, k.SecretHash))
                .ToArray();
        }

        public string? GetFirstActiveApiKeyRaw(string userId)
        {
            return ApiKeys
                .Where(k => k.UserId == userId && k.RevokedAtUtc is null && k.RawKey is not null)
                .OrderByDescending(k => k.CreatedAtUtc)
                .Select(k => k.RawKey)
                .FirstOrDefault();
        }
    }
}
