using FluentAssertions;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
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

        mapped.Id.Should().Be(user.Id);
        mapped.Email.Should().Be("user@example.com");
        mapped.Role.Should().Be(UserRole.Admin);
        mapped.Status.Should().Be(UserStatus.Disabled);
        mapped.PasswordHash.Should().Be("hash");
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

        mapped.IsUsed.Should().BeTrue();
        mapped.UsedAtUtc.Should().Be(usedAt);
    }

    [Fact]
    public void ApiKey_RoundTrip_PreservesLastUsedAndRevokedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var apiKey = new ApiKey("key-1", "user-1", "default", "orr_1234", "secret-hash", now.AddDays(-1));
        apiKey.MarkUsed(now);
        apiKey.Revoke(now.AddMinutes(1));

        var mapped = AuthPersistenceMapper.ToDomain(AuthPersistenceMapper.ToEntity(apiKey));

        mapped.Id.Should().Be(apiKey.Id);
        mapped.UserId.Should().Be(apiKey.UserId);
        mapped.Name.Should().Be(apiKey.Name);
        mapped.Prefix.Should().Be(apiKey.Prefix);
        mapped.SecretHash.Should().Be(apiKey.SecretHash);
        mapped.LastUsedAtUtc.Should().Be(now);
        mapped.RevokedAtUtc.Should().Be(now.AddMinutes(1));
        mapped.IsActive.Should().BeFalse();
    }
}
