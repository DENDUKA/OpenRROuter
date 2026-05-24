using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class GoogleAuthServiceTests
{
    private readonly InMemoryAuthRepository _repo = new();
    private readonly ISecretGenerator _secretGenerator = new SecretGenerator();

    [Fact]
    public async Task LoginOrRegisterWithGoogle_NewUser_CreatesAccountAndReturnsSession()
    {
        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-new", "new@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.Token.Should().StartWith("orr_session_");
        result.Session.Role.Should().Be(UserRole.User);

        var user = await _repo.GetUserByEmailAsync("new@example.com");
        user.Should().NotBeNull();
        user!.Email.Should().Be("new@example.com");
        user.Role.Should().Be(UserRole.User);
        user.PasswordHash.Should().BeNull();

        var login = await _repo.GetExternalLoginAsync("google", "google-sub-new");
        login.Should().NotBeNull();
        login!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_ExistingGoogleUser_ReturnsSession()
    {
        var existingUser = UserAccount.CreateForExternalLogin(
            "user-existing", "existing@example.com", UserRole.Admin, DateTimeOffset.UtcNow);
        var existingLogin = new ExternalLogin(
            "login-1", existingUser.Id, "google", "google-sub-existing", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(existingUser, existingLogin);

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-existing", "existing@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-existing");
        result.Session.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_ExistingEmailUser_LinksAccountAndPreservesRole()
    {
        var invitedUser = new UserAccount(
            "user-invited", "invited@example.com", UserRole.Admin,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        await _repo.AddUserAndMarkInviteUsedAsync(invitedUser, MakeDummyInvite());

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-invited", "invited@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-invited");
        result.Session.Role.Should().Be(UserRole.Admin);

        var link = await _repo.GetExternalLoginAsync("google", "google-sub-invited");
        link.Should().NotBeNull();
        link!.UserId.Should().Be("user-invited");
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_DisabledExistingEmailUser_ReturnsUserDisabled()
    {
        var disabledUser = new UserAccount(
            "user-disabled", "disabled@example.com", UserRole.User,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        disabledUser.Disable();
        await _repo.AddUserAndMarkInviteUsedAsync(disabledUser, MakeDummyInvite());

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-disabled", "disabled@example.com");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_DisabledExistingGoogleUser_ReturnsUserDisabled()
    {
        var disabledUser = UserAccount.CreateForExternalLogin(
            "user-google-disabled", "gdisabled@example.com", UserRole.User, DateTimeOffset.UtcNow);
        disabledUser.Disable();
        var existingLogin = new ExternalLogin(
            "login-disabled", disabledUser.Id, "google", "google-sub-gdisabled", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(disabledUser, existingLogin);

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-gdisabled", "gdisabled@example.com");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
    }

    private static Invite MakeDummyInvite() => new(
        "invite-dummy", "dummy@example.com", UserRole.User,
        "dummy-token-hash",
        DateTimeOffset.UtcNow.AddDays(7),
        "admin-1",
        DateTimeOffset.UtcNow);
}
