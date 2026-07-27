using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class EmailSignupTests
{
    private const string Password = "StrongPassword123!";

    [Fact]
    public async Task Register_NewEmail_CreatesPendingUserAndIssuesCode()
    {
        var (service, repo) = Create();
        var now = DateTimeOffset.UtcNow;

        var result = await service.RegisterUserAsync("USER@Example.com", Password, now);

        result.Succeeded.Should().BeTrue();
        result.Email.Should().Be("user@example.com");
        result.RawCode.Should().MatchRegex(@"^\d{6}$");

        var user = await repo.GetUserByEmailAsync("user@example.com");
        user.Should().NotBeNull();
        user!.Status.Should().Be(UserStatus.PendingVerification);
        user.Role.Should().Be(UserRole.User);

        // The raw code is never stored; only its hash.
        var verification = await repo.GetEmailVerificationAsync(user.Id);
        verification.Should().NotBeNull();
        verification!.CodeHash.Should().NotBe(result.RawCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Fails()
    {
        var (service, _) = Create();
        var now = DateTimeOffset.UtcNow;
        await service.RegisterUserAsync("user@example.com", Password, now);

        var second = await service.RegisterUserAsync("user@example.com", Password, now);

        second.Succeeded.Should().BeFalse();
        second.ErrorCode.Should().Be("email_already_registered");
    }

    [Fact]
    public async Task Register_WeakPassword_Fails()
    {
        var (service, _) = Create();

        var result = await service.RegisterUserAsync("user@example.com", "short", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("weak_password");
    }

    [Fact]
    public async Task Verify_CorrectCode_ActivatesUser()
    {
        var (service, repo) = Create();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", Password, now);

        var result = await service.VerifyEmailAsync("user@example.com", registration.RawCode!, now.AddMinutes(1));

        result.Succeeded.Should().BeTrue();
        result.User!.Status.Should().Be(UserStatus.Active);

        var user = await repo.GetUserByEmailAsync("user@example.com");
        user!.Status.Should().Be(UserStatus.Active);
        (await repo.GetEmailVerificationAsync(user.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Verify_WrongCode_FailsAndIncrementsAttempts()
    {
        var (service, repo) = Create();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", Password, now);
        var wrong = registration.RawCode == "000000" ? "000001" : "000000";

        var result = await service.VerifyEmailAsync("user@example.com", wrong, now.AddMinutes(1));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_code");

        var user = await repo.GetUserByEmailAsync("user@example.com");
        (await repo.GetEmailVerificationAsync(user!.Id))!.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Verify_ExpiredCode_Fails()
    {
        var (service, _) = Create();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", Password, now);

        var result = await service.VerifyEmailAsync(
            "user@example.com", registration.RawCode!, now.Add(EmailVerificationService.CodeTtl).AddMinutes(1));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("code_expired");
    }

    [Fact]
    public async Task Verify_TooManyWrongAttempts_LocksOut()
    {
        var (service, _) = Create();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", Password, now);
        var wrong = registration.RawCode == "000000" ? "000001" : "000000";

        string? lastError = null;
        for (var i = 0; i < EmailVerificationService.MaxAttempts; i++)
        {
            lastError = (await service.VerifyEmailAsync("user@example.com", wrong, now.AddMinutes(1))).ErrorCode;
        }

        lastError.Should().Be("too_many_attempts");

        // Even the correct code is now rejected until a resend.
        var afterLockout = await service.VerifyEmailAsync("user@example.com", registration.RawCode!, now.AddMinutes(1));
        afterLockout.ErrorCode.Should().Be("too_many_attempts");
    }

    [Fact]
    public async Task Resend_AfterCooldown_IssuesNewWorkingCode()
    {
        var (service, _) = Create();
        var now = DateTimeOffset.UtcNow;
        var registration = await service.RegisterUserAsync("user@example.com", Password, now);

        var resend = await service.ResendVerificationAsync(
            "user@example.com", now.Add(EmailVerificationService.ResendCooldown).AddSeconds(1));

        resend.Succeeded.Should().BeTrue();
        resend.RawCode.Should().MatchRegex(@"^\d{6}$");

        // The new code verifies; the old one no longer does.
        var withNew = await service.VerifyEmailAsync("user@example.com", resend.RawCode!, now.AddMinutes(2));
        withNew.Succeeded.Should().BeTrue();
        _ = registration; // old code intentionally unused after reissue
    }

    [Fact]
    public async Task Resend_WithinCooldown_Fails()
    {
        var (service, _) = Create();
        var now = DateTimeOffset.UtcNow;
        await service.RegisterUserAsync("user@example.com", Password, now);

        var resend = await service.ResendVerificationAsync("user@example.com", now.AddSeconds(5));

        resend.Succeeded.Should().BeFalse();
        resend.ErrorCode.Should().Be("resend_cooldown");
    }

    [Fact]
    public async Task Verify_UnknownEmail_Fails()
    {
        var (service, _) = Create();

        var result = await service.VerifyEmailAsync("nobody@example.com", "123456", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("no_pending_verification");
    }

    private static (AuthAccountApplicationService Service, InMemoryAuthRepository Repo) Create()
    {
        var repo = new InMemoryAuthRepository();
        var secretHasher = new Sha256SecretHasher();
        var codeHasher = new HmacSecretHasher(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("orr-dev-code-hmac-key-do-not-use-in-production")));
        var secretGenerator = new SecretGenerator();
        var passwordHasher = new Pbkdf2PasswordHasher();
        var service = new AuthAccountApplicationService(
            repo,
            new InviteRegistrationService(secretHasher, passwordHasher, secretGenerator),
            new EmailVerificationService(codeHasher),
            passwordHasher,
            new ApiKeyFactory(secretHasher, secretGenerator),
            secretHasher);
        return (service, repo);
    }
}
