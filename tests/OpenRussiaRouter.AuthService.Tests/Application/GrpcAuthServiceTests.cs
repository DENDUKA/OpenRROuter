using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class GrpcAuthServiceTests
{
    [Fact]
    public async Task ValidateApiKey_ValidKey_ReturnsGrpcResponse()
    {
        var repo = new InMemoryAuthRepository();
        var service = new GrpcAuthService(
            new StubValidator(new ValidateApiKeyResult(true, "user-1", "key-1", "sk-or-abc123", null)),
            repo,
            CreateAccounts(),
            CreateLoginService(),
            new GoogleAuthService(repo, new SecretGenerator()),
            NullLogger<GrpcAuthService>.Instance);

        var response = await service.ValidateApiKey(new ValidateApiKeyRequest { RawKey = "orr_test" }, context: null!);

        response.IsValid.Should().BeTrue();
        response.UserId.Should().Be("user-1");
        response.ApiKeyId.Should().Be("key-1");
        response.ApiKeyPrefix.Should().Be("sk-or-abc123");
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task ValidateApiKey_InvalidKey_ReturnsErrorCode()
    {
        var repo2 = new InMemoryAuthRepository();
        var service = new GrpcAuthService(
            new StubValidator(new ValidateApiKeyResult(false, null, null, null, "invalid_api_key")),
            repo2,
            CreateAccounts(),
            CreateLoginService(),
            new GoogleAuthService(repo2, new SecretGenerator()),
            NullLogger<GrpcAuthService>.Instance);

        var response = await service.ValidateApiKey(new ValidateApiKeyRequest { RawKey = "orr_bad" }, context: null!);

        response.IsValid.Should().BeFalse();
        response.UserId.Should().Be(string.Empty);
        response.ApiKeyId.Should().Be(string.Empty);
        response.ApiKeyPrefix.Should().Be(string.Empty);
        response.ErrorCode.Should().Be("invalid_api_key");
    }

    [Fact]
    public async Task InviteAndApiKeyMethods_PersistWorkflow_ReturnSuccessResponses()
    {
        var accounts = CreateAccounts();
        var sharedRepo = new InMemoryAuthRepository();
        var service = new GrpcAuthService(new StubValidator(new ValidateApiKeyResult(false, null, null, null, "unused")), sharedRepo, accounts, CreateLoginService(), new GoogleAuthService(sharedRepo, new SecretGenerator()), NullLogger<GrpcAuthService>.Instance);

        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "USER@example.com",
            Role = "Admin",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);
        var user = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);
        var apiKey = await service.CreateApiKey(new CreateApiKeyRequest
        {
            UserId = user.UserId,
            Name = "default"
        }, context: null!);
        var revoked = await service.RevokeApiKey(new RevokeApiKeyRequest
        {
            UserId = user.UserId,
            ApiKeyId = apiKey.ApiKeyId
        }, context: null!);

        invite.IsCreated.Should().BeTrue();
        invite.RawToken.Should().NotBeNullOrEmpty();
        invite.Email.Should().Be("user@example.com");
        invite.Role.Should().Be("Admin");
        user.IsCompleted.Should().BeTrue();
        user.Email.Should().Be("user@example.com");
        user.Role.Should().Be("Admin");
        apiKey.IsCreated.Should().BeTrue();
        apiKey.RawKey.Should().StartWith("sk-or-");
        apiKey.Prefix.Should().NotBeNullOrEmpty();
        revoked.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task CreateInvite_InvalidRole_ReturnsValidationError()
    {
        var accounts = CreateAccounts();
        var invalidRoleRepo = new InMemoryAuthRepository();
        var service = new GrpcAuthService(new StubValidator(new ValidateApiKeyResult(false, null, null, null, "unused")), invalidRoleRepo, accounts, CreateLoginService(), new GoogleAuthService(invalidRoleRepo, new SecretGenerator()), NullLogger<GrpcAuthService>.Instance);

        var response = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "user@example.com",
            Role = "Owner",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);

        response.IsCreated.Should().BeFalse();
        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsSuccessWithSessionToken()
    {
        var (service, _) = CreateServiceWithSharedRepo();
        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "login@example.com",
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);
        _ = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);

        var response = await service.Login(new LoginRequest
        {
            Email = "login@example.com",
            Password = "StrongPassword123!"
        }, context: null!);

        response.Succeeded.Should().BeTrue();
        response.SessionToken.Should().StartWith("orr_session");
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsInvalidCredentials()
    {
        var (service, _) = CreateServiceWithSharedRepo();

        var response = await service.Login(new LoginRequest
        {
            Email = "nobody@example.com",
            Password = "AnyPassword123!"
        }, context: null!);

        response.Succeeded.Should().BeFalse();
        response.ErrorCode.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task CompleteInvite_InvalidToken_ReturnsValidationFailed()
    {
        var (service, _) = CreateServiceWithSharedRepo();

        var response = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = "totally-bogus-token",
            Password = "StrongPassword123!"
        }, context: null!);

        response.IsCompleted.Should().BeFalse();
        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task RevokeApiKey_NonExistentKey_ReturnsValidationFailed()
    {
        var (service, _) = CreateServiceWithSharedRepo();

        var response = await service.RevokeApiKey(new RevokeApiKeyRequest
        {
            UserId = "user-1",
            ApiKeyId = "key-does-not-exist"
        }, context: null!);

        response.IsRevoked.Should().BeFalse();
        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task RenameApiKey_Success_ReturnsIsRenamed()
    {
        var (service, _) = CreateServiceWithSharedRepo();
        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "rename@example.com",
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);
        var user = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);
        var apiKey = await service.CreateApiKey(new CreateApiKeyRequest
        {
            UserId = user.UserId,
            Name = "original"
        }, context: null!);

        var response = await service.RenameApiKey(new RenameApiKeyRequest
        {
            UserId = user.UserId,
            ApiKeyId = apiKey.ApiKeyId,
            Name = "renamed"
        }, context: null!);

        response.IsRenamed.Should().BeTrue();
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task RenameApiKey_NonExistentKey_ReturnsValidationFailed()
    {
        var (service, _) = CreateServiceWithSharedRepo();

        var response = await service.RenameApiKey(new RenameApiKeyRequest
        {
            UserId = "user-1",
            ApiKeyId = "key-does-not-exist",
            Name = "new-name"
        }, context: null!);

        response.IsRenamed.Should().BeFalse();
        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task ListApiKeys_BeforeAnyKeysCreated_ReturnsEmptyList()
    {
        var (service, _) = CreateServiceWithSharedRepo();

        var response = await service.ListApiKeys(
            new ListApiKeysRequest { UserId = "user-without-keys" },
            context: null!);

        response.Keys.Should().BeEmpty();
    }

    private sealed class StubValidator(ValidateApiKeyResult result) : IAuthApiKeyValidator
    {
        public Task<ValidateApiKeyResult> ValidateAsync(string rawKey, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private static AuthAccountApplicationService CreateAccounts()
    {
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        return new AuthAccountApplicationService(
            new InMemoryAuthRepository(),
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
    }

    private static IAuthRepository CreateInMemoryAuthRepository() => new InMemoryAuthRepository();

    private static LoginService CreateLoginService()
    {
        return new LoginService(new Pbkdf2PasswordHasher(), new SecretGenerator());
    }

    private static (GrpcAuthService Service, InMemoryAuthRepository Repo) CreateServiceWithSharedRepo()
    {
        var repo = new InMemoryAuthRepository();
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var accounts = new AuthAccountApplicationService(
            repo,
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var service = new GrpcAuthService(
            new StubValidator(new ValidateApiKeyResult(false, null, null, null, "unused")),
            repo,
            accounts,
            new LoginService(new Pbkdf2PasswordHasher(), secretGenerator),
            new GoogleAuthService(repo, secretGenerator),
            NullLogger<GrpcAuthService>.Instance);
        return (service, repo);
    }
}
