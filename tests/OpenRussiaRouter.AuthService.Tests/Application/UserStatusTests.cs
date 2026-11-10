using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Grpc;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class UserStatusTests
{
    [Fact]
    public async Task UpdateUser_StatusDisabled_ChangesStatusToDisabled()
    {
        var (service, _, _) = CreateServiceWithSharedRepo();
        var user = await CreateUserAsync(service, "disable-test@example.com");

        var updateResponse = await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            Status = "Disabled"
        }, context: null!);

        updateResponse.Succeeded.Should().BeTrue();

        var getResponse = await service.GetUser(new GetUserRequest { UserId = user.UserId }, context: null!);
        getResponse.User.Status.Should().Be("Disabled");
    }

    [Fact]
    public async Task UpdateUser_StatusActive_ReenablesUser()
    {
        var (service, _, _) = CreateServiceWithSharedRepo();
        var user = await CreateUserAsync(service, "reenable-test@example.com");

        await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            Status = "Disabled"
        }, context: null!);

        var reenableResponse = await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            Status = "Active"
        }, context: null!);

        reenableResponse.Succeeded.Should().BeTrue();

        var getResponse = await service.GetUser(new GetUserRequest { UserId = user.UserId }, context: null!);
        getResponse.User.Status.Should().Be("Active");
    }

    [Fact]
    public async Task UpdateUser_InvalidStatus_ReturnsValidationFailed()
    {
        var (service, _, _) = CreateServiceWithSharedRepo();
        var user = await CreateUserAsync(service, "invalid-status@example.com");

        var updateResponse = await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            Status = "NonExistentStatus"
        }, context: null!);

        updateResponse.Succeeded.Should().BeFalse();
        updateResponse.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task UpdateUser_OnlyStatus_DoesNotResetOtherFields()
    {
        var (service, _, _) = CreateServiceWithSharedRepo();
        var user = await CreateUserAsync(service, "status-keep-fields@example.com");

        await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            DisplayName = "Custom Name",
            MaxApiKeys = 25,
            DisplayCurrency = "EUR"
        }, context: null!);

        await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            Status = "Disabled"
        }, context: null!);

        var getResponse = await service.GetUser(new GetUserRequest { UserId = user.UserId }, context: null!);
        getResponse.User.Status.Should().Be("Disabled");
        getResponse.User.DisplayName.Should().Be("Custom Name");
        getResponse.User.MaxApiKeys.Should().Be(25);
        getResponse.User.DisplayCurrency.Should().Be("EUR");
    }

    [Fact]
    public async Task DisabledUser_ApiKeyValidation_FailsAndKeysReportedInactive()
    {
        var (service, repo, validator) = CreateServiceWithSharedRepo();
        var user = await CreateUserAsync(service, "disabled-key-test@example.com");

        var createKeyResponse = await service.CreateApiKey(new CreateApiKeyRequest
        {
            UserId = user.UserId,
            Name = "test-key"
        }, context: null!);

        createKeyResponse.IsCreated.Should().BeTrue();
        var rawKey = createKeyResponse.RawKey;
        var keyId = createKeyResponse.ApiKeyId;

        // Key should be valid initially
        var initialValid = await validator.ValidateAsync(rawKey);
        initialValid.IsValid.Should().BeTrue();

        // Key should be reported active in raw key lists
        var activeKeysBefore = await repo.ListAllActiveApiKeysRawAsync();
        activeKeysBefore.Should().Contain(k => k.ApiKeyId == keyId);

        var nonDeletedKeysBefore = await repo.ListAllNonDeletedApiKeysRawAsync();
        nonDeletedKeysBefore.Should().Contain(k => k.ApiKeyId == keyId && k.IsActive);

        // Disable the user
        await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            Status = "Disabled"
        }, context: null!);

        // Key validation must now fail
        var afterValid = await validator.ValidateAsync(rawKey);
        afterValid.IsValid.Should().BeFalse();

        // Key must not appear in active keys
        var activeKeysAfter = await repo.ListAllActiveApiKeysRawAsync();
        activeKeysAfter.Should().NotContain(k => k.ApiKeyId == keyId);

        // Key in non-deleted keys must have IsActive = false
        var nonDeletedKeysAfter = await repo.ListAllNonDeletedApiKeysRawAsync();
        nonDeletedKeysAfter.Should().Contain(k => k.ApiKeyId == keyId && !k.IsActive);
    }

    private static async Task<CompleteInviteResponse> CreateUserAsync(GrpcAuthService service, string email)
    {
        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = email,
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);

        return await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);
    }

    private static (GrpcAuthService Service, InMemoryAuthRepository Repo, DapperAuthApiKeyValidator Validator) CreateServiceWithSharedRepo()
    {
        var repo = new InMemoryAuthRepository();
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var accounts = new AuthAccountApplicationService(
            repo,
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new EmailVerificationService(HmacSecretHasher.CreateDev()),
            new Pbkdf2PasswordHasher(),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var validator = new DapperAuthApiKeyValidator(repo, secretHasher);
        var service = new GrpcAuthService(
            validator,
            repo,
            accounts,
            new LoginService(new Pbkdf2PasswordHasher(), secretGenerator),
            new ExternalLoginService(repo, secretGenerator),
            NullLogger<GrpcAuthService>.Instance);
        return (service, repo, validator);
    }
}
