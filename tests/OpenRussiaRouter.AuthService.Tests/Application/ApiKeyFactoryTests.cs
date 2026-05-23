using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class ApiKeyFactoryTests
{
    private readonly ApiKeyFactory _factory = new(new Sha256SecretHasher(), new SecretGenerator());

    [Fact]
    public void Create_ValidInput_ReturnsRawKeyOnceAndStoredHash()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);

        created.RawKey.Should().StartWith("sk-or-");
        created.ApiKey.Prefix.Should().StartWith("sk-or-");
        created.RawKey.Should().NotBe(created.ApiKey.SecretHash);
        created.ApiKey.Name.Should().Be("default");
        created.ApiKey.IsActive.Should().BeTrue();
    }

    [Fact]
    public void VerifyAndMarkUsed_ActiveMatchingKey_ReturnsTrueAndUpdatesLastUsedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _factory.Create("user-1", "default", now);

        var result = _factory.VerifyAndMarkUsed(created.RawKey, created.ApiKey, now.AddMinutes(1));

        result.Should().BeTrue();
        created.ApiKey.LastUsedAtUtc.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void VerifyAndMarkUsed_WrongKey_ReturnsFalse()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);

        var result = _factory.VerifyAndMarkUsed("orr_wrong", created.ApiKey, DateTimeOffset.UtcNow);

        result.Should().BeFalse();
        created.ApiKey.LastUsedAtUtc.Should().BeNull();
    }

    [Fact]
    public void VerifyAndMarkUsed_RevokedKey_ReturnsFalse()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);
        created.ApiKey.Revoke(DateTimeOffset.UtcNow);

        var result = _factory.VerifyAndMarkUsed(created.RawKey, created.ApiKey, DateTimeOffset.UtcNow);

        result.Should().BeFalse();
    }

    [Fact]
    public void Rename_DoesNotChangeSecretHash()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);
        var hash = created.ApiKey.SecretHash;

        created.ApiKey.Rename("renamed");

        created.ApiKey.Name.Should().Be("renamed");
        created.ApiKey.SecretHash.Should().Be(hash);
    }
}
