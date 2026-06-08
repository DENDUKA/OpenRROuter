using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Grpc;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class DisplayCurrencyTests
{
    [Fact]
    public async Task GetUser_NewUser_ReturnsDefaultRubCurrency()
    {
        var (service, _) = CreateServiceWithSharedRepo();
        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "currency@example.com",
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);
        var user = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);

        var response = await service.GetUser(new GetUserRequest { UserId = user.UserId }, context: null!);

        response.User.Should().NotBeNull();
        response.User.HasDisplayCurrency.Should().BeTrue();
        response.User.DisplayCurrency.Should().Be("RUB");
    }

    [Fact]
    public async Task UpdateUser_WithDisplayCurrency_ChangesValue()
    {
        var (service, _) = CreateServiceWithSharedRepo();
        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "currency2@example.com",
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);
        var user = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);

        var updateResponse = await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            DisplayCurrency = "USD"
        }, context: null!);
        var getResponse = await service.GetUser(new GetUserRequest { UserId = user.UserId }, context: null!);

        updateResponse.Succeeded.Should().BeTrue();
        getResponse.User.DisplayCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task UpdateUser_OnlyDisplayCurrency_DoesNotResetDisplayNameOrMaxApiKeys()
    {
        var (service, _) = CreateServiceWithSharedRepo();
        var invite = await service.CreateInvite(new CreateInviteRequest
        {
            Email = "currency3@example.com",
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        }, context: null!);
        var user = await service.CompleteInvite(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        }, context: null!);

        // Set display_name and max_api_keys first
        await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            DisplayName = "Test User",
            MaxApiKeys = 5
        }, context: null!);

        // Now update only display_currency — display_name and max_api_keys must not be reset
        await service.UpdateUser(new UpdateUserRequest
        {
            UserId = user.UserId,
            DisplayCurrency = "EUR"
        }, context: null!);

        var getResponse = await service.GetUser(new GetUserRequest { UserId = user.UserId }, context: null!);

        getResponse.User.DisplayCurrency.Should().Be("EUR");
        getResponse.User.HasDisplayName.Should().BeTrue();
        getResponse.User.DisplayName.Should().Be("Test User");
        getResponse.User.MaxApiKeys.Should().Be(5);
    }

    private static (GrpcAuthService Service, InMemoryAuthRepository Repo) CreateServiceWithSharedRepo()
    {
        var repo = new InMemoryAuthRepository();
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var accounts = new AuthAccountApplicationService(
            repo,
            new InviteRegistrationService(secretHasher, new Pbkdf2PasswordHasher(), secretGenerator),
            new EmailVerificationService(secretHasher),
            new Pbkdf2PasswordHasher(),
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var service = new GrpcAuthService(
            new StubValidator(),
            repo,
            accounts,
            new LoginService(new Pbkdf2PasswordHasher(), secretGenerator),
            new GoogleAuthService(repo, secretGenerator),
            NullLogger<GrpcAuthService>.Instance);
        return (service, repo);
    }

    private sealed class StubValidator : IAuthApiKeyValidator
    {
        public Task<ValidateApiKeyResult> ValidateAsync(string rawKey, CancellationToken ct = default) =>
            Task.FromResult(new ValidateApiKeyResult(false, null, null, null, "unused"));
    }
}
