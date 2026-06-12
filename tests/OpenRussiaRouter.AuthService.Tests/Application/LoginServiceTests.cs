using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class LoginServiceTests
{
    private readonly Pbkdf2PasswordHasher _passwordHasher = new();
    private readonly ISecretGenerator _secretGenerator = new SecretGenerator();

    [Fact]
    public void Login_ValidCredentials_ReturnsSession()
    {
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser("StrongPassword123!", now);
        var service = new LoginService(_passwordHasher, _secretGenerator);

        var result = service.Login(user, "StrongPassword123!", now, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.Token.Should().StartWith("orr_session_");
        result.Session.UserId.Should().Be(user.Id);
        result.Session.Role.Should().Be(user.Role);
        result.Session.ExpiresAtUtc.Should().Be(now.AddHours(8));
    }

    [Fact]
    public void Login_UnknownUser_ReturnsInvalidCredentials()
    {
        var service = new LoginService(_passwordHasher, _secretGenerator);

        var result = service.Login(null, "StrongPassword123!", DateTimeOffset.UtcNow, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeFalse();
        result.Session.Should().BeNull();
        result.ErrorCode.Should().Be("invalid_credentials");
    }

    [Fact]
    public void Login_WrongPassword_ReturnsInvalidCredentials()
    {
        var user = CreateUser("StrongPassword123!", DateTimeOffset.UtcNow);
        var service = new LoginService(_passwordHasher, _secretGenerator);

        var result = service.Login(user, "WrongPassword123!", DateTimeOffset.UtcNow, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeFalse();
        result.Session.Should().BeNull();
        result.ErrorCode.Should().Be("invalid_credentials");
    }

    [Fact]
    public void Login_DisabledUser_ReturnsUserDisabled()
    {
        var user = CreateUser("StrongPassword123!", DateTimeOffset.UtcNow);
        user.Disable();
        var service = new LoginService(_passwordHasher, _secretGenerator);

        var result = service.Login(user, "StrongPassword123!", DateTimeOffset.UtcNow, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeFalse();
        result.Session.Should().BeNull();
        result.ErrorCode.Should().Be("user_disabled");
    }

    [Fact]
    public void Login_PendingVerificationUser_ReturnsEmailNotVerified()
    {
        var now = DateTimeOffset.UtcNow;
        var user = UserAccount.CreateForEmailSignup(
            "user-1", "user@example.com", UserRole.User, _passwordHasher.HashPassword("StrongPassword123!"), now);
        var service = new LoginService(_passwordHasher, _secretGenerator);

        var result = service.Login(user, "StrongPassword123!", now, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeFalse();
        result.Session.Should().BeNull();
        result.ErrorCode.Should().Be("email_not_verified");
    }

    [Fact]
    public void Login_MaxFailedAttempts_LocksAccount()
    {
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser("StrongPassword123!", now);
        var service = new LoginService(_passwordHasher, _secretGenerator);

        string? lastError = null;
        for (var i = 0; i < LoginService.MaxFailedAttempts; i++)
        {
            lastError = service.Login(user, "WrongPassword123!", now, TimeSpan.FromHours(8)).ErrorCode;
        }

        lastError.Should().Be("account_locked");
        user.IsLockedOut(now).Should().BeTrue();
        user.LockedUntilUtc.Should().Be(now.Add(LoginService.LockoutDuration));
    }

    [Fact]
    public void Login_LockedAccount_RejectsEvenCorrectPassword()
    {
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser("StrongPassword123!", now);
        var service = new LoginService(_passwordHasher, _secretGenerator);
        for (var i = 0; i < LoginService.MaxFailedAttempts; i++)
        {
            service.Login(user, "WrongPassword123!", now, TimeSpan.FromHours(8));
        }

        var result = service.Login(user, "StrongPassword123!", now.AddMinutes(1), TimeSpan.FromHours(8));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("account_locked");
    }

    [Fact]
    public void Login_AfterLockoutExpires_SucceedsAndResetsThrottle()
    {
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser("StrongPassword123!", now);
        var service = new LoginService(_passwordHasher, _secretGenerator);
        for (var i = 0; i < LoginService.MaxFailedAttempts; i++)
        {
            service.Login(user, "WrongPassword123!", now, TimeSpan.FromHours(8));
        }

        var afterExpiry = now.Add(LoginService.LockoutDuration).AddSeconds(1);
        var result = service.Login(user, "StrongPassword123!", afterExpiry, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeTrue();
        user.FailedLoginCount.Should().Be(0);
        user.LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void Login_SuccessAfterSomeFailures_ResetsCounter()
    {
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser("StrongPassword123!", now);
        var service = new LoginService(_passwordHasher, _secretGenerator);
        service.Login(user, "WrongPassword123!", now, TimeSpan.FromHours(8));
        service.Login(user, "WrongPassword123!", now, TimeSpan.FromHours(8));

        var result = service.Login(user, "StrongPassword123!", now, TimeSpan.FromHours(8));

        result.Succeeded.Should().BeTrue();
        user.FailedLoginCount.Should().Be(0);
        user.LockedUntilUtc.Should().BeNull();
    }

    private UserAccount CreateUser(string password, DateTimeOffset createdAtUtc)
    {
        return new UserAccount(
            "user-1",
            "user@example.com",
            UserRole.Admin,
            _passwordHasher.HashPassword(password),
            createdAtUtc);
    }
}
