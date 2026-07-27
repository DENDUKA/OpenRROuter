using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class ProductAnalyticsEmissionTests
{
    [Fact]
    public async Task VerifyEmail_Success_EmitsUserVerifiedWithEmailSignupMethod()
    {
        var (service, repository, analytics) = CreateService();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", "StrongPassword123!", now);
        var user = await repository.GetUserByEmailAsync("user@example.com");

        var result = await service.VerifyEmailAsync("user@example.com", registration.RawCode!, now.AddMinutes(1));

        result.ErrorCode.Should().BeNull();
        result.Succeeded.Should().BeTrue();
        await WaitUntil(() => analytics.UserVerifiedCalls.Count == 1);
        var call = analytics.UserVerifiedCalls.Single();
        call.UserId.Should().Be(user!.Id);
        call.SignupMethod.Should().Be("email");
    }

    [Fact]
    public async Task VerifyEmail_Failure_DoesNotEmitUserVerified()
    {
        var (service, _, analytics) = CreateService();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", "StrongPassword123!", now);
        var wrong = registration.RawCode == "000000" ? "000001" : "000000";

        var result = await service.VerifyEmailAsync("user@example.com", wrong, now.AddMinutes(1));

        result.Succeeded.Should().BeFalse();
        analytics.UserVerifiedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateApiKey_ActiveUser_EmitsApiKeyCreated()
    {
        var (service, repository, analytics) = CreateService();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;

        var result = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        await WaitUntil(() => analytics.ApiKeyCreatedCalls.Count == 1);
        analytics.ApiKeyCreatedCalls.Single().Should().Be("user-1");
    }

    [Fact]
    public async Task CreateApiKey_DisabledUser_DoesNotEmitApiKeyCreated()
    {
        var (service, repository, analytics) = CreateService();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        user.Disable();
        repository.Users[user.Id] = user;

        var result = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        analytics.ApiKeyCreatedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyticsThrows_VerifyEmailStillSucceeds()
    {
        var (service, _) = CreateServiceWithThrowingAnalytics();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", "StrongPassword123!", now);

        var result = await service.VerifyEmailAsync("user@example.com", registration.RawCode!, now.AddMinutes(1));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyticsThrows_CreateApiKeyStillSucceeds()
    {
        var (service, repository) = CreateServiceWithThrowingAnalytics();
        var user = new UserAccount("user-1", "user@example.com", UserRole.User, "hash", DateTimeOffset.UtcNow);
        repository.Users[user.Id] = user;

        var result = await service.CreateApiKeyAsync("user-1", "default", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleLogin_NewUser_EmitsUserVerifiedWithGoogleSignupMethod()
    {
        var repository = new InMemoryAuthRepository();
        var analytics = new RecordingProductAnalytics();
        var sut = new ExternalLoginService(repository, new SecretGenerator(), analytics, NullLogger<ExternalLoginService>.Instance);

        var result = await sut.LoginOrRegisterAsync("google","google-sub-new", "new@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        await WaitUntil(() => analytics.UserVerifiedCalls.Count == 1);
        var call = analytics.UserVerifiedCalls.Single();
        call.SignupMethod.Should().Be("google");
        var user = await repository.GetUserByEmailAsync("new@example.com");
        call.UserId.Should().Be(user!.Id);
    }

    [Fact]
    public async Task GoogleLogin_ReturningUser_DoesNotEmitUserVerified()
    {
        var repository = new InMemoryAuthRepository();
        var existingUser = UserAccount.CreateForExternalLogin(
            "user-existing", "existing@example.com", UserRole.User, DateTimeOffset.UtcNow);
        var existingLogin = new ExternalLogin(
            "login-1", existingUser.Id, "google", "google-sub-existing", DateTimeOffset.UtcNow);
        await repository.AddUserWithExternalLoginAsync(existingUser, existingLogin);
        var analytics = new RecordingProductAnalytics();
        var sut = new ExternalLoginService(repository, new SecretGenerator(), analytics, NullLogger<ExternalLoginService>.Instance);

        var result = await sut.LoginOrRegisterAsync("google","google-sub-existing", "existing@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        await Task.Delay(50);
        analytics.UserVerifiedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GoogleLogin_ExistingEmailUser_DoesNotEmitUserVerified()
    {
        var repository = new InMemoryAuthRepository();
        var invitedUser = new UserAccount(
            "user-invited", "invited@example.com", UserRole.User,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        await repository.AddUserAndMarkInviteUsedAsync(invitedUser, MakeDummyInvite());
        var analytics = new RecordingProductAnalytics();
        var sut = new ExternalLoginService(repository, new SecretGenerator(), analytics, NullLogger<ExternalLoginService>.Instance);

        var result = await sut.LoginOrRegisterAsync("google","google-sub-invited", "invited@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        await Task.Delay(50);
        analytics.UserVerifiedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyticsThrows_GoogleLoginStillSucceeds()
    {
        var repository = new InMemoryAuthRepository();
        var sut = new ExternalLoginService(repository, new SecretGenerator(), new ThrowingProductAnalytics(), NullLogger<ExternalLoginService>.Instance);

        var result = await sut.LoginOrRegisterAsync("google","google-sub-throw", "throw@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
    }

    private static (AuthAccountApplicationService Service, TestAuthRepository Repository, RecordingProductAnalytics Analytics) CreateService()
    {
        var repository = new TestAuthRepository();
        var analytics = new RecordingProductAnalytics();
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var passwordHasher = new Pbkdf2PasswordHasher();
        var service = new AuthAccountApplicationService(
            repository,
            new InviteRegistrationService(secretHasher, passwordHasher, secretGenerator),
            new EmailVerificationService(HmacSecretHasher.CreateDev()),
            passwordHasher,
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher,
            analytics,
            NullLogger<AuthAccountApplicationService>.Instance);
        return (service, repository, analytics);
    }

    private static (AuthAccountApplicationService Service, TestAuthRepository Repository) CreateServiceWithThrowingAnalytics()
    {
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var passwordHasher = new Pbkdf2PasswordHasher();
        var repository = new TestAuthRepository();
        var service = new AuthAccountApplicationService(
            repository,
            new InviteRegistrationService(secretHasher, passwordHasher, secretGenerator),
            new EmailVerificationService(HmacSecretHasher.CreateDev()),
            passwordHasher,
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher,
            new ThrowingProductAnalytics(),
            NullLogger<AuthAccountApplicationService>.Instance);
        return (service, repository);
    }

    private static async Task WaitUntil(Func<bool> predicate, int timeoutMs = 2000)
    {
        var started = DateTimeOffset.UtcNow;
        while (!predicate())
        {
            if ((DateTimeOffset.UtcNow - started).TotalMilliseconds > timeoutMs)
                throw new TimeoutException("Predicate was not satisfied in time.");
            await Task.Delay(10);
        }
    }

    private static Invite MakeDummyInvite() => new(
        "invite-dummy", "dummy@example.com", UserRole.User,
        "dummy-token-hash",
        DateTimeOffset.UtcNow.AddDays(7),
        "admin-1",
        DateTimeOffset.UtcNow);

    private sealed class RecordingProductAnalytics : IProductAnalytics
    {
        private readonly object _lock = new();
        private readonly List<(string UserId, string SignupMethod)> _userVerified = [];
        private readonly List<string> _apiKeyCreated = [];

        public IReadOnlyList<(string UserId, string SignupMethod)> UserVerifiedCalls
        {
            get { lock (_lock) { return _userVerified.ToArray(); } }
        }

        public IReadOnlyList<string> ApiKeyCreatedCalls
        {
            get { lock (_lock) { return _apiKeyCreated.ToArray(); } }
        }

        public void UserVerified(string userId, string signupMethod)
        {
            lock (_lock) { _userVerified.Add((userId, signupMethod)); }
        }

        public void ApiKeyCreated(string userId)
        {
            lock (_lock) { _apiKeyCreated.Add(userId); }
        }

        public void ApiRequestCompleted(string userId, ApiRequestAnalytics data)
        {
        }

        public void PaymentCompleted(string userId, PaymentAnalytics data)
        {
        }
    }

    private sealed class ThrowingProductAnalytics : IProductAnalytics
    {
        public void UserVerified(string userId, string signupMethod) => throw new InvalidOperationException("analytics failure");

        public void ApiKeyCreated(string userId) => throw new InvalidOperationException("analytics failure");

        public void ApiRequestCompleted(string userId, ApiRequestAnalytics data) => throw new InvalidOperationException("analytics failure");

        public void PaymentCompleted(string userId, PaymentAnalytics data) => throw new InvalidOperationException("analytics failure");
    }

    private sealed class TestAuthRepository : IAuthRepository
    {
        public Dictionary<string, UserAccount> Users { get; } = new(StringComparer.Ordinal);
        public List<Invite> Invites { get; } = [];
        public List<ApiKey> ApiKeys { get; } = [];
        public Dictionary<string, EmailVerification> EmailVerifications { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EmailVerification> PasswordResets { get; } = new(StringComparer.Ordinal);

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

        public Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> DisableApiKeyAsync(string userId, string apiKeyId, DateTimeOffset disabledAtUtc, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> EnableApiKeyAsync(string userId, string apiKeyId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> DeleteApiKeyAsync(string userId, string apiKeyId, DateTimeOffset deletedAtUtc, CancellationToken ct = default) => Task.FromResult(false);

        public Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<(string, string)>>(Array.Empty<(string, string)>());

        public Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name)>> ListAllActiveApiKeysRawAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<(string, string, string, string)>>(Array.Empty<(string, string, string, string)>());

        public Task<IReadOnlyCollection<(string ApiKeyId, string RawKey, string UserId, string Name, bool IsActive)>> ListAllNonDeletedApiKeysRawAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<(string, string, string, string, bool)>>(Array.Empty<(string, string, string, string, bool)>());

        public Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default) => Task.FromResult<ApiKey?>(null);

        public Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default) => Task.FromResult<ExternalLogin?>(null);

        public Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default) => Task.CompletedTask;

        public Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

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

        public Task UpdateLoginThrottleAsync(string userId, int failedLoginCount, DateTimeOffset? lockedUntilUtc, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdatePasswordHashAsync(string userId, string passwordHash, CancellationToken ct = default)
        {
            if (Users.TryGetValue(userId, out var user))
            {
                Users[userId] = UserAccount.Restore(
                    user.Id, user.Email, user.Role, passwordHash, user.Status, user.CreatedAtUtc,
                    user.DisplayName, user.MaxApiKeys, user.DisplayCurrency);
            }
            return Task.CompletedTask;
        }

        public Task UpsertPasswordResetAsync(EmailVerification reset, CancellationToken ct = default)
        {
            PasswordResets[reset.UserId] = reset;
            return Task.CompletedTask;
        }

        public Task<EmailVerification?> GetPasswordResetAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(PasswordResets.GetValueOrDefault(userId));

        public Task UpdatePasswordResetAsync(EmailVerification reset, CancellationToken ct = default)
        {
            PasswordResets[reset.UserId] = reset;
            return Task.CompletedTask;
        }

        public Task UpdatePasswordAndDeleteResetAsync(string userId, string passwordHash, CancellationToken ct = default)
        {
            PasswordResets.Remove(userId);
            return Task.CompletedTask;
        }
    }
}
