using OpenRussiaRouter.AuthService.Domain;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;

namespace OpenRussiaRouter.AuthService.Tests.Infrastructure.Persistence;

public sealed class AuthPersistenceMapperTests
{
    [Fact]
    public void UserAccount_RoundTrip_PreservesDisabledStatus()
    {
        var user = new UserAccount("user-1", "USER@example.com", UserRole.Admin, "hash", DateTimeOffset.UtcNow);
        user.Disable();

        var mapped = AuthPersistenceMapper.ToDomain(AuthPersistenceMapper.ToEntity(user));

        Assert.Equal(user.Id, mapped.Id);
        Assert.Equal("user@example.com", mapped.Email);
        Assert.Equal(UserRole.Admin, mapped.Role);
        Assert.Equal(UserStatus.Disabled, mapped.Status);
        Assert.Equal("hash", mapped.PasswordHash);
    }

    [Fact]
    public void Invite_RoundTrip_PreservesUsedAt()
    {
        var usedAt = DateTimeOffset.UtcNow;
        var invite = new Invite(
            "invite-1",
            "user@example.com",
            UserRole.User,
            "token-hash",
            usedAt.AddDays(1),
            "admin-1",
            usedAt.AddDays(-1));
        invite.MarkUsed(usedAt);

        var mapped = AuthPersistenceMapper.ToDomain(AuthPersistenceMapper.ToEntity(invite));

        Assert.True(mapped.IsUsed);
        Assert.Equal(usedAt, mapped.UsedAtUtc);
    }

    [Fact]
    public void ApiKey_RoundTrip_PreservesLastUsedAndRevokedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var apiKey = new ApiKey("key-1", "user-1", "default", "orr_1234", "secret-hash", now.AddDays(-1));
        apiKey.MarkUsed(now);
        apiKey.Revoke(now.AddMinutes(1));

        var mapped = AuthPersistenceMapper.ToDomain(AuthPersistenceMapper.ToEntity(apiKey));

        Assert.Equal(apiKey.Id, mapped.Id);
        Assert.Equal(apiKey.UserId, mapped.UserId);
        Assert.Equal(apiKey.Name, mapped.Name);
        Assert.Equal(apiKey.Prefix, mapped.Prefix);
        Assert.Equal(apiKey.SecretHash, mapped.SecretHash);
        Assert.Equal(now, mapped.LastUsedAtUtc);
        Assert.Equal(now.AddMinutes(1), mapped.RevokedAtUtc);
        Assert.False(mapped.IsActive);
    }
}
