using OpenRussiaRouter.AuthService.Application;
using OpenRussiaRouter.AuthService.Domain;
using OpenRussiaRouter.AuthService.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class LoginServiceTests
{
    private readonly Pbkdf2PasswordHasher _passwordHasher = new();

    [Fact]
    public void Login_ValidCredentials_ReturnsSession()
    {
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser("StrongPassword123!", now);
        var service = new LoginService(_passwordHasher);

        var result = service.Login(user, "StrongPassword123!", now, TimeSpan.FromHours(8));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Session);
        Assert.StartsWith("orr_session_", result.Session.Token, StringComparison.Ordinal);
        Assert.Equal(user.Id, result.Session.UserId);
        Assert.Equal(user.Role, result.Session.Role);
        Assert.Equal(now.AddHours(8), result.Session.ExpiresAtUtc);
    }

    [Fact]
    public void Login_UnknownUser_ReturnsInvalidCredentials()
    {
        var service = new LoginService(_passwordHasher);

        var result = service.Login(null, "StrongPassword123!", DateTimeOffset.UtcNow, TimeSpan.FromHours(8));

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public void Login_WrongPassword_ReturnsInvalidCredentials()
    {
        var user = CreateUser("StrongPassword123!", DateTimeOffset.UtcNow);
        var service = new LoginService(_passwordHasher);

        var result = service.Login(user, "WrongPassword123!", DateTimeOffset.UtcNow, TimeSpan.FromHours(8));

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public void Login_DisabledUser_ReturnsUserDisabled()
    {
        var user = CreateUser("StrongPassword123!", DateTimeOffset.UtcNow);
        user.Disable();
        var service = new LoginService(_passwordHasher);

        var result = service.Login(user, "StrongPassword123!", DateTimeOffset.UtcNow, TimeSpan.FromHours(8));

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Equal("user_disabled", result.ErrorCode);
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

