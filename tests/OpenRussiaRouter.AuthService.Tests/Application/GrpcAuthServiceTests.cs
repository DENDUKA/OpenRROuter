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
        var service = new GrpcAuthService(
            new StubValidator(new ValidateApiKeyResult(true, "user-1", "key-1", "sk-or-abc123", null)),
            new InMemoryAuthRepository(),
            CreateAccounts(),
            CreateLoginService());

        var response = await service.ValidateApiKey(new ValidateApiKeyRequest { RawKey = "orr_test" }, context: null!);

        Assert.True(response.IsValid);
        Assert.Equal("user-1", response.UserId);
        Assert.Equal("key-1", response.ApiKeyId);
        Assert.Equal("sk-or-abc123", response.ApiKeyPrefix);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task ValidateApiKey_InvalidKey_ReturnsErrorCode()
    {
        var service = new GrpcAuthService(
            new StubValidator(new ValidateApiKeyResult(false, null, null, null, "invalid_api_key")),
            new InMemoryAuthRepository(),
            CreateAccounts(),
            CreateLoginService());

        var response = await service.ValidateApiKey(new ValidateApiKeyRequest { RawKey = "orr_bad" }, context: null!);

        Assert.False(response.IsValid);
        Assert.Equal(string.Empty, response.UserId);
        Assert.Equal(string.Empty, response.ApiKeyId);
        Assert.Equal(string.Empty, response.ApiKeyPrefix);
        Assert.Equal("invalid_api_key", response.ErrorCode);
    }

    [Fact]
    public async Task InviteAndApiKeyMethods_PersistWorkflow_ReturnSuccessResponses()
    {
        var accounts = CreateAccounts();
        var service = new GrpcAuthService(new StubValidator(new ValidateApiKeyResult(false, null, null, null, "unused")), CreateInMemoryAuthRepository(), accounts, CreateLoginService());

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

        Assert.True(invite.IsCreated);
        Assert.NotEmpty(invite.RawToken);
        Assert.Equal("user@example.com", invite.Email);
        Assert.Equal("Admin", invite.Role);
        Assert.True(user.IsCompleted);
        Assert.Equal("user@example.com", user.Email);
        Assert.Equal("Admin", user.Role);
        Assert.True(apiKey.IsCreated);
        Assert.StartsWith("sk-or-", apiKey.RawKey, StringComparison.Ordinal);
        Assert.NotEmpty(apiKey.Prefix);
        Assert.True(revoked.IsRevoked);
    }

    [Fact]
    public async Task CreateInvite_InvalidRole_ReturnsValidationError()
    {
        var accounts = CreateAccounts();
        var service = new GrpcAuthService(new StubValidator(new ValidateApiKeyResult(false, null, null, null, "unused")), CreateInMemoryAuthRepository(), accounts, CreateLoginService());

        var response = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "user@example.com",
            Role = "Owner",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);

        Assert.False(response.IsCreated);
        Assert.Equal("validation_failed", response.ErrorCode);
    }

    private sealed class StubValidator(ValidateApiKeyResult result) : IAuthApiKeyValidator
    {
        public ValidateApiKeyResult Validate(string rawKey) => result;
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
}
