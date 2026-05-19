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

        Assert.StartsWith("sk-or-", created.RawKey, StringComparison.Ordinal);
        Assert.StartsWith("sk-or-", created.ApiKey.Prefix, StringComparison.Ordinal);
        Assert.NotEqual(created.RawKey, created.ApiKey.SecretHash);
        Assert.Equal("default", created.ApiKey.Name);
        Assert.True(created.ApiKey.IsActive);
    }

    [Fact]
    public void VerifyAndMarkUsed_ActiveMatchingKey_ReturnsTrueAndUpdatesLastUsedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var created = _factory.Create("user-1", "default", now);

        var result = _factory.VerifyAndMarkUsed(created.RawKey, created.ApiKey, now.AddMinutes(1));

        Assert.True(result);
        Assert.Equal(now.AddMinutes(1), created.ApiKey.LastUsedAtUtc);
    }

    [Fact]
    public void VerifyAndMarkUsed_WrongKey_ReturnsFalse()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);

        var result = _factory.VerifyAndMarkUsed("orr_wrong", created.ApiKey, DateTimeOffset.UtcNow);

        Assert.False(result);
        Assert.Null(created.ApiKey.LastUsedAtUtc);
    }

    [Fact]
    public void VerifyAndMarkUsed_RevokedKey_ReturnsFalse()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);
        created.ApiKey.Revoke(DateTimeOffset.UtcNow);

        var result = _factory.VerifyAndMarkUsed(created.RawKey, created.ApiKey, DateTimeOffset.UtcNow);

        Assert.False(result);
    }

    [Fact]
    public void Rename_DoesNotChangeSecretHash()
    {
        var created = _factory.Create("user-1", "default", DateTimeOffset.UtcNow);
        var hash = created.ApiKey.SecretHash;

        created.ApiKey.Rename("renamed");

        Assert.Equal("renamed", created.ApiKey.Name);
        Assert.Equal(hash, created.ApiKey.SecretHash);
    }
}

