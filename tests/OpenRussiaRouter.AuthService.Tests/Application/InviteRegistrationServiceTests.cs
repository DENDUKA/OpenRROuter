using OpenRussiaRouter.AuthService.Application;
using OpenRussiaRouter.AuthService.Domain;
using OpenRussiaRouter.AuthService.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class InviteRegistrationServiceTests
{
    private readonly InviteRegistrationService _service = new(new Sha256SecretHasher(), new Pbkdf2PasswordHasher());

    [Fact]
    public void CreateInvite_ValidInput_ReturnsInviteAndRawToken()
    {
        var now = DateTimeOffset.UtcNow;

        var result = _service.CreateInvite(
            "USER@Example.COM ",
            UserRole.User,
            "admin-1",
            now.AddDays(1),
            now);

        Assert.Equal("user@example.com", result.Invite.Email);
        Assert.Equal(UserRole.User, result.Invite.Role);
        Assert.StartsWith("orr_invite_", result.RawToken, StringComparison.Ordinal);
        Assert.NotEqual(result.RawToken, result.Invite.TokenHash);
        Assert.False(result.Invite.IsUsed);
    }

    [Fact]
    public void CompleteInvite_ValidToken_CreatesUserAndMarksInviteUsed()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.Admin, "admin-1", now.AddDays(1), now);

        var user = _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(1));

        Assert.Equal("user@example.com", user.Email);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.True(created.Invite.IsUsed);
        Assert.NotEqual("StrongPassword123!", user.PasswordHash);
    }

    [Fact]
    public void CompleteInvite_UsedInvite_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);
        _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(2)));
    }

    [Fact]
    public void CompleteInvite_ExpiredInvite_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddMinutes(1), now);

        Assert.Throws<InvalidOperationException>(() =>
            _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(2)));
    }

    [Fact]
    public void CompleteInvite_InvalidToken_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);

        Assert.Throws<InvalidOperationException>(() =>
            _service.CompleteInvite(created.Invite, "wrong-token", "StrongPassword123!", now.AddMinutes(1)));
    }
}

