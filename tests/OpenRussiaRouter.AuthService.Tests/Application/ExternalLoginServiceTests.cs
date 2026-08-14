using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class ExternalLoginServiceTests
{
    private readonly InMemoryAuthRepository _repo = new();
    private readonly ISecretGenerator _secretGenerator = new SecretGenerator();

    [Fact]
    public async Task LoginOrRegister_Google_NewUser_CreatesAccountAndReturnsSession()
    {
        var sut = new ExternalLoginService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterAsync("google", "google-sub-new", "new@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.Token.Should().StartWith("orr_session_");
        result.Session.Role.Should().Be(UserRole.User);
        // This is the only branch that creates an account, and the only one the web funnel may
        // count as a signup.
        result.IsNewUser.Should().BeTrue();

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
    public async Task LoginOrRegister_Google_ExistingGoogleUser_ReturnsSession()
    {
        var existingUser = UserAccount.CreateForExternalLogin(
            "user-existing", "existing@example.com", UserRole.Admin, DateTimeOffset.UtcNow);
        var existingLogin = new ExternalLogin(
            "login-1", existingUser.Id, "google", "google-sub-existing", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(existingUser, existingLogin);

        var sut = new ExternalLoginService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterAsync("google", "google-sub-existing", "existing@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-existing");
        result.Session.Role.Should().Be(UserRole.Admin);
        result.IsNewUser.Should().BeFalse("signing in again is not a registration");
    }

    [Fact]
    public async Task LoginOrRegister_Google_ExistingEmailUser_LinksAccountAndPreservesRole()
    {
        var invitedUser = new UserAccount(
            "user-invited", "invited@example.com", UserRole.Admin,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        await _repo.AddUserAndMarkInviteUsedAsync(invitedUser, MakeDummyInvite());

        var sut = new ExternalLoginService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterAsync("google", "google-sub-invited", "invited@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-invited");
        result.Session.Role.Should().Be(UserRole.Admin);
        // Linking a provider to an account that already existed would double-count the funnel.
        result.IsNewUser.Should().BeFalse();

        var link = await _repo.GetExternalLoginAsync("google", "google-sub-invited");
        link.Should().NotBeNull();
        link!.UserId.Should().Be("user-invited");
    }

    [Fact]
    public async Task LoginOrRegister_Google_DisabledExistingEmailUser_ReturnsUserDisabled()
    {
        var disabledUser = new UserAccount(
            "user-disabled", "disabled@example.com", UserRole.User,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        disabledUser.Disable();
        await _repo.AddUserAndMarkInviteUsedAsync(disabledUser, MakeDummyInvite());

        var sut = new ExternalLoginService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterAsync("google", "google-sub-disabled", "disabled@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task LoginOrRegister_Google_DisabledExistingGoogleUser_ReturnsUserDisabled()
    {
        var disabledUser = UserAccount.CreateForExternalLogin(
            "user-google-disabled", "gdisabled@example.com", UserRole.User, DateTimeOffset.UtcNow);
        disabledUser.Disable();
        var existingLogin = new ExternalLogin(
            "login-disabled", disabledUser.Id, "google", "google-sub-gdisabled", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(disabledUser, existingLogin);

        var sut = new ExternalLoginService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterAsync("google", "google-sub-gdisabled", "gdisabled@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
    }

    [Fact]
    public async Task LoginOrRegister_Google_PendingVerificationEmailUser_ActivatesAccount()
    {
        var pendingUser = UserAccount.CreateForEmailSignup(
            "user-pending", "pending@example.com", UserRole.User,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        await _repo.AddUserWithEmailVerificationAsync(pendingUser, new EmailVerification(
            pendingUser.Id, "hash", DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow, 0));

        var sut = new ExternalLoginService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterAsync("google", "google-sub-pending", "pending@example.com", DateTimeOffset.UtcNow);

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.UserId.Should().Be("user-pending");

        var user = await _repo.GetUserAsync("user-pending");
        user!.Status.Should().Be(UserStatus.Active);

        var verification = await _repo.GetEmailVerificationAsync("user-pending");
        verification.Should().BeNull();

        var link = await _repo.GetExternalLoginAsync("google", "google-sub-pending");
        link.Should().NotBeNull();
        link!.UserId.Should().Be("user-pending");
    }

    private static Invite MakeDummyInvite() => new(
        "invite-dummy", "dummy@example.com", UserRole.User,
        "dummy-token-hash",
        DateTimeOffset.UtcNow.AddDays(7),
        "admin-1",
        DateTimeOffset.UtcNow);
}
