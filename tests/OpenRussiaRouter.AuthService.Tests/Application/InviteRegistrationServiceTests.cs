using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class InviteRegistrationServiceTests
{
    private readonly InviteRegistrationService _service = new(new Sha256SecretHasher(), new Pbkdf2PasswordHasher(), new SecretGenerator());

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

        result.Invite.Email.Should().Be("user@example.com");
        result.Invite.Role.Should().Be(UserRole.User);
        result.RawToken.Should().StartWith("rwt_invite_");
        result.RawToken.Should().NotBe(result.Invite.TokenHash);
        result.Invite.IsUsed.Should().BeFalse();
    }

    [Fact]
    public void CompleteInvite_ValidToken_CreatesUserAndMarksInviteUsed()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.Admin, "admin-1", now.AddDays(1), now);

        var user = _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(1));

        user.Email.Should().Be("user@example.com");
        user.Role.Should().Be(UserRole.Admin);
        created.Invite.IsUsed.Should().BeTrue();
        user.PasswordHash.Should().NotBe("StrongPassword123!");
    }

    [Fact]
    public void CompleteInvite_UsedInvite_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);
        _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(1));

        ((Action)(() =>
            _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(2)))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CompleteInvite_ExpiredInvite_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddMinutes(1), now);

        ((Action)(() =>
            _service.CompleteInvite(created.Invite, created.RawToken, "StrongPassword123!", now.AddMinutes(2)))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CompleteInvite_InvalidToken_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);

        ((Action)(() =>
            _service.CompleteInvite(created.Invite, "wrong-token", "StrongPassword123!", now.AddMinutes(1)))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CompleteInvite_WeakPassword_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _service.CreateInvite("user@example.com", UserRole.User, "admin-1", now.AddDays(1), now);

        ((Action)(() =>
            _service.CompleteInvite(created.Invite, created.RawToken, "short", now.AddMinutes(1)))).Should().Throw<ArgumentException>();
    }
}
