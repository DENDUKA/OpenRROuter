using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class AuthAccountApplicationServiceTests
{
    [Fact]
    public async Task CreateInvite_StoresInviteAndReturnsRawTokenOnce()
    {
        var repository = new TestAuthRepository();
        var service = CreateService(repository);
        var now = DateTimeOffset.UtcNow;

        var created = await service.CreateInviteAsync("USER@example.com", UserRole.User, "admin-1", now.AddDays(1), now);

        created.RawToken.Should().StartWith("orr_invite_");
        repository.Invites.Should().ContainSingle();
        created.RawToken.Should().NotBe(repository.Invites.Single().TokenHash);
        repository.Invites.Single().Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task CompleteInvite_ValidToken_StoresUserAndMarksInviteUsed()
    {
        var repository = new TestAuthRepository();
        var service = CreateService(repository);
        var now = DateTimeOffset.UtcNow;
        var created = await service.CreateInviteAsync("user@example.com", UserRole.Admin, "admin-1", now.AddDays(1), now);

        var user = await service.CompleteInviteAsync(created.RawToken, "StrongPassword123!", now.AddMinutes(1));

        user.Email.Should().Be("user@example.com");
        user.Role.Should().Be(UserRole.Admin);
        repository.Users.Should().ContainSingle();
        repository.Invites.Single().IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateApiKey_ActiveUser_StoresHashAndReturnsRawKey()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;
        var service = CreateService(repository);

        var created = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);

        created.RawKey.Should().StartWith("sk-or-");
        repository.ApiKeys.Should().ContainSingle();
        created.ApiKey.SecretHash.Should().Be(repository.ApiKeys.Single().SecretHash);
        created.RawKey.Should().NotBe(repository.ApiKeys.Single().SecretHash);
    }

    [Fact]
    public async Task CreateApiKey_DisabledUser_Throws()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        user.Disable();
        repository.Users[user.Id] = user;
        var service = CreateService(repository);

        await ((Func<Task>)(() =>
            service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RenameAndRevokeApiKey_ExistingKey_UpdatesMetadata()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;
        var service = CreateService(repository);
        var created = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);
        var revokedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        await service.RenameApiKeyAsync("user-1", created.ApiKey.Id, "renamed");
        await service.RevokeApiKeyAsync("user-1", created.ApiKey.Id, revokedAt);

        var key = repository.ApiKeys.Should().ContainSingle().Which;
        key.Name.Should().Be("renamed");
        key.RevokedAtUtc.Should().Be(revokedAt);
    }

    [Fact]
    public async Task DeleteApiKey_RevokedKey_HidesFromListButKeepsRow()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;
        var service = CreateService(repository);
        var created = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        await service.RevokeApiKeyAsync("user-1", created.ApiKey.Id, now);

        await service.DeleteApiKeyAsync("user-1", created.ApiKey.Id, now.AddMinutes(1));

        repository.ApiKeys.Single().IsDeleted.Should().BeTrue();
        (await service.ListApiKeysAsync("user-1")).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteApiKey_ActiveKey_Throws()
    {
        var repository = new TestAuthRepository();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;
        var service = CreateService(repository);
        var created = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);

        await ((Func<Task>)(() =>
            service.DeleteApiKeyAsync("user-1", created.ApiKey.Id, DateTimeOffset.UtcNow))).Should().ThrowAsync<KeyNotFoundException>();
        repository.ApiKeys.Single().IsDeleted.Should().BeFalse();
    }

    private static AuthAccountApplicationService CreateService(TestAuthRepository repository)
    {
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var passwordHasher = new Pbkdf2PasswordHasher();
        return new AuthAccountApplicationService(
            repository,
            new InviteRegistrationService(secretHasher, passwordHasher, secretGenerator),
            new EmailVerificationService(secretHasher),
            passwordHasher,
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
    }

    private sealed class TestAuthRepository : IAuthRepository
    {
        public Dictionary<string, UserAccount> Users { get; } = new(StringComparer.Ordinal);
        public List<Invite> Invites { get; } = [];
        public List<ApiKey> ApiKeys { get; } = [];

        public Task AddInviteAsync(Invite invite, CancellationToken ct = default)
        {
            Invites.Add(invite);
            return Task.CompletedTask;
        }

        public Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(Invites.SingleOrDefault(invite => invite.TokenHash == tokenHash));

        public Task AddUserAndMarkInviteUsedAsync(UserAccount user, Invite invite, CancellationToken ct = default)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(userId));

        public Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(Users.Values.SingleOrDefault(u => u.Email == email.ToLowerInvariant()));

        public Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, string? afterUserId = null, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyCollection<UserAccount>, bool)>((Users.Values.ToArray(), false));

        public Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default)
        {
            ApiKeys.Add(apiKey);
            return Task.CompletedTask;
        }

        public Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default) =>
            Task.FromResult(ApiKeys.SingleOrDefault(key => key.Id == apiKeyId));

        public Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, string? afterApiKeyId = null, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyCollection<ApiKey>, bool)>((ApiKeys.Where(key => key.UserId == userId && (includeDeleted || !key.IsDeleted)).ToArray(), false));

        public Task UpdateUserAsync(UserAccount user, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> CountApiKeysAsync(string userId, CancellationToken ct = default) => 
            Task.FromResult(ApiKeys.Count(key => key.UserId == userId && !key.IsDeleted));

        public Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default)
        {
            var key = ApiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null) return Task.FromResult(false);
            key.Rename(name);
            return Task.FromResult(true);
        }

        public Task<bool> RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default)
        {
            var key = ApiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null) return Task.FromResult(false);
            key.Revoke(revokedAtUtc);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteApiKeyAsync(string userId, string apiKeyId, DateTimeOffset deletedAtUtc, CancellationToken ct = default)
        {
            var key = ApiKeys.SingleOrDefault(candidate => candidate.UserId == userId && candidate.Id == apiKeyId);
            if (key is null || key.IsActive || key.IsDeleted) return Task.FromResult(false);
            key.Delete(deletedAtUtc);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<(string, string)>>(
                ApiKeys.Where(k => k.RevokedAtUtc is null).Select(k => (k.Prefix, k.SecretHash)).ToArray());

        public Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name)>> ListAllActiveApiKeysRawAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<(string, string, string, string)>>(
                ApiKeys.Where(k => k.RevokedAtUtc is null && k.RawKey is not null).Select(k => (k.Id, k.RawKey!, k.UserId, k.Name)).ToArray());

        public Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(ApiKeys
                .Where(k => k.UserId == userId && k.RevokedAtUtc is null && k.RawKey is not null)
                .OrderByDescending(k => k.CreatedAtUtc)
                .Select(k => k.RawKey)
                .FirstOrDefault());

        public Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default) =>
            Task.FromResult(ApiKeys.SingleOrDefault(k => k.SecretHash == secretHash && k.RevokedAtUtc is null));

        public Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default)
        {
            ApiKeys.SingleOrDefault(k => k.Id == apiKeyId)?.MarkUsed(lastUsedAtUtc);
            return Task.CompletedTask;
        }

        public Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
            => Task.FromResult<ExternalLogin?>(null);

        public Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Dictionary<string, EmailVerification> EmailVerifications { get; } = new(StringComparer.Ordinal);

        public Task AddUserWithEmailVerificationAsync(UserAccount user, EmailVerification verification, CancellationToken ct = default)
        {
            Users[user.Id] = user;
            EmailVerifications[verification.UserId] = verification;
            return Task.CompletedTask;
        }

        public Task<EmailVerification?> GetEmailVerificationAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(EmailVerifications.GetValueOrDefault(userId));

        public Task UpdateEmailVerificationAsync(EmailVerification verification, CancellationToken ct = default)
        {
            EmailVerifications[verification.UserId] = verification;
            return Task.CompletedTask;
        }

        public Task ActivateUserAndDeleteVerificationAsync(string userId, CancellationToken ct = default)
        {
            if (Users.TryGetValue(userId, out var user)) user.Activate();
            EmailVerifications.Remove(userId);
            return Task.CompletedTask;
        }
    }
}
