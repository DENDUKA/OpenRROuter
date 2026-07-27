using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class PasswordResetTests
{
    private const string Password = "StrongPassword123!";
    private const string NewPassword = "EvenStrongerPassword456!";
    private const string Email = "user@example.com";

    [Fact]
    public async Task Request_ActiveUser_IssuesCode()
    {
        var (service, repo, _) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;

        var result = await service.RequestPasswordResetAsync(Email, now);

        result.Succeeded.Should().BeTrue();
        result.Email.Should().Be(Email);
        result.RawCode.Should().MatchRegex(@"^\d{6}$");

        // Only the hash is stored.
        var user = await repo.GetUserByEmailAsync(Email);
        var reset = await repo.GetPasswordResetAsync(user!.Id);
        reset.Should().NotBeNull();
        reset!.CodeHash.Should().NotBe(result.RawCode);
    }

    [Fact]
    public async Task Request_UnknownEmail_FailsWithoutDisclosure()
    {
        var (service, _, _) = await CreateWithActiveUserAsync();

        var result = await service.RequestPasswordResetAsync("nobody@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("password_reset_not_available");
    }

    [Fact]
    public async Task Request_PendingVerificationUser_Fails()
    {
        var (service, _, _) = Create();
        await service.RegisterUserAsync("pending@example.com", Password, DateTimeOffset.UtcNow);

        var result = await service.RequestPasswordResetAsync("pending@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("password_reset_not_available");
    }

    [Fact]
    public async Task Request_WithinCooldown_Fails()
    {
        var (service, _, _) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        await service.RequestPasswordResetAsync(Email, now);

        var second = await service.RequestPasswordResetAsync(Email, now.AddSeconds(5));

        second.Succeeded.Should().BeFalse();
        second.ErrorCode.Should().Be("resend_cooldown");
    }

    [Fact]
    public async Task Request_AfterCooldown_ReissuesWorkingCode()
    {
        var (service, _, _) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        await service.RequestPasswordResetAsync(Email, now);

        var reissued = await service.RequestPasswordResetAsync(
            Email, now.Add(EmailVerificationService.ResendCooldown).AddSeconds(1));

        reissued.Succeeded.Should().BeTrue();

        var result = await service.ResetPasswordAsync(Email, reissued.RawCode!, NewPassword, now.AddMinutes(2));
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_CorrectCode_ChangesPasswordAndDeletesReset()
    {
        var (service, repo, login) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        var request = await service.RequestPasswordResetAsync(Email, now);

        var result = await service.ResetPasswordAsync(Email, request.RawCode!, NewPassword, now.AddMinutes(1));

        result.Succeeded.Should().BeTrue();
        var user = await repo.GetUserByEmailAsync(Email);
        (await repo.GetPasswordResetAsync(user!.Id)).Should().BeNull();

        login(user, Password).Succeeded.Should().BeFalse();
        login(user, NewPassword).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_WrongCode_FailsAndCountsAttempt()
    {
        var (service, repo, _) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        var request = await service.RequestPasswordResetAsync(Email, now);
        var wrong = request.RawCode == "000000" ? "000001" : "000000";

        var result = await service.ResetPasswordAsync(Email, wrong, NewPassword, now.AddMinutes(1));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_code");

        var user = await repo.GetUserByEmailAsync(Email);
        (await repo.GetPasswordResetAsync(user!.Id))!.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Reset_ExpiredCode_Fails()
    {
        var (service, _, _) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        var request = await service.RequestPasswordResetAsync(Email, now);

        var result = await service.ResetPasswordAsync(
            Email, request.RawCode!, NewPassword, now.Add(EmailVerificationService.CodeTtl).AddMinutes(1));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("code_expired");
    }

    [Fact]
    public async Task Reset_WeakNewPassword_Fails()
    {
        var (service, _, _) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        var request = await service.RequestPasswordResetAsync(Email, now);

        var result = await service.ResetPasswordAsync(Email, request.RawCode!, "short", now.AddMinutes(1));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("weak_password");
    }

    [Fact]
    public async Task Reset_WithoutPendingReset_Fails()
    {
        var (service, _, _) = await CreateWithActiveUserAsync();

        var result = await service.ResetPasswordAsync(Email, "123456", NewPassword, DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("no_pending_reset");
    }

    [Fact]
    public async Task Reset_ClearsLoginLockout()
    {
        var (service, repo, login) = await CreateWithActiveUserAsync();
        var now = DateTimeOffset.UtcNow;
        var user = await repo.GetUserByEmailAsync(Email);
        for (var i = 0; i < LoginService.MaxFailedAttempts; i++)
        {
            login(user, "WrongPassword123!");
        }
        login(user, Password).ErrorCode.Should().Be("account_locked");

        var request = await service.RequestPasswordResetAsync(Email, now);
        var reset = await service.ResetPasswordAsync(Email, request.RawCode!, NewPassword, now.AddMinutes(1));
        reset.Succeeded.Should().BeTrue();

        var refreshed = await repo.GetUserByEmailAsync(Email);
        login(refreshed, NewPassword).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Change_CorrectCurrentPassword_ChangesPassword()
    {
        var (service, repo, login) = await CreateWithActiveUserAsync();
        var user = await repo.GetUserByEmailAsync(Email);

        var result = await service.ChangePasswordAsync(user!.Id, Password, NewPassword);

        result.Succeeded.Should().BeTrue();
        var refreshed = await repo.GetUserByEmailAsync(Email);
        login(refreshed, Password).Succeeded.Should().BeFalse();
        login(refreshed, NewPassword).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Change_WrongCurrentPassword_Fails()
    {
        var (service, repo, _) = await CreateWithActiveUserAsync();
        var user = await repo.GetUserByEmailAsync(Email);

        var result = await service.ChangePasswordAsync(user!.Id, "WrongPassword123!", NewPassword);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Change_WeakNewPassword_Fails()
    {
        var (service, repo, _) = await CreateWithActiveUserAsync();
        var user = await repo.GetUserByEmailAsync(Email);

        var result = await service.ChangePasswordAsync(user!.Id, Password, "short");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("weak_password");
    }

    private static (AuthAccountApplicationService Service, InMemoryAuthRepository Repo, Func<UserAccount?, string, LoginResult> Login) Create()
    {
        var repo = new InMemoryAuthRepository();
        var secretHasher = new Sha256SecretHasher();
        var secretGenerator = new SecretGenerator();
        var passwordHasher = new Pbkdf2PasswordHasher();
        var service = new AuthAccountApplicationService(
            repo,
            new InviteRegistrationService(secretHasher, passwordHasher, secretGenerator),
            new EmailVerificationService(HmacSecretHasher.CreateDev()),
            passwordHasher,
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        var loginService = new LoginService(passwordHasher, secretGenerator);
        LoginResult Login(UserAccount? user, string password) =>
            loginService.Login(user, password, DateTimeOffset.UtcNow, TimeSpan.FromHours(8));
        return (service, repo, Login);
    }

    private static async Task<(AuthAccountApplicationService Service, InMemoryAuthRepository Repo, Func<UserAccount?, string, LoginResult> Login)> CreateWithActiveUserAsync()
    {
        var created = Create();
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var registration = await created.Service.RegisterUserAsync(Email, Password, now);
        await created.Service.VerifyEmailAsync(Email, registration.RawCode!, now);
        return created;
    }
}
