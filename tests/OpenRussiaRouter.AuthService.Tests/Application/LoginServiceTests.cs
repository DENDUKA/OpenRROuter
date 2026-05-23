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
