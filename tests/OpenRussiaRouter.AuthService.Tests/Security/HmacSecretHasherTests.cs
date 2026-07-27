using System.Security.Cryptography;
using FluentAssertions;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Security;

public sealed class HmacSecretHasherTests
{
    [Fact]
    public void Hash_ReturnsBase64String()
    {
        var hasher = HmacSecretHasher.CreateDev();

        var hash = hasher.Hash("123456");

        hash.Should().NotBeNullOrWhiteSpace();
        // Should be valid base64
        var bytes = Convert.FromBase64String(hash);
        bytes.Should().HaveCount(32); // HMAC-SHA256 output
    }

    [Fact]
    public void Verify_CorrectSecret_ReturnsTrue()
    {
        var hasher = HmacSecretHasher.CreateDev();
        var hash = hasher.Hash("123456");

        var result = hasher.Verify("123456", hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var hasher = HmacSecretHasher.CreateDev();
        var hash = hasher.Hash("123456");

        var result = hasher.Verify("654321", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var hasher = HmacSecretHasher.CreateDev();

        var hash1 = hasher.Hash("123456");
        var hash2 = hasher.Hash("123456");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Hash_DifferentKeysProduceDifferentHashes()
    {
        var key1 = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("key1"));
        var key2 = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("key2"));
        var hasher1 = new HmacSecretHasher(key1);
        var hasher2 = new HmacSecretHasher(key2);

        var hash1 = hasher1.Hash("123456");
        var hash2 = hasher2.Hash("123456");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Verify_EmptyHash_ReturnsFalse()
    {
        var hasher = HmacSecretHasher.CreateDev();

        hasher.Verify("123456", "").Should().BeFalse();
        hasher.Verify("123456", null!).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptySecret_ReturnsFalse()
    {
        var hasher = HmacSecretHasher.CreateDev();
        var hash = hasher.Hash("123456");

        hasher.Verify("", hash).Should().BeFalse();
        hasher.Verify(null!, hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_InvalidBase64_ReturnsFalse()
    {
        var hasher = HmacSecretHasher.CreateDev();

        hasher.Verify("123456", "not-a-base64-hash!!!").Should().BeFalse();
    }

    [Fact]
    public void Hash_ThrowsOnEmptySecret()
    {
        var hasher = HmacSecretHasher.CreateDev();

        ((Action)(() => hasher.Hash(""))).Should().Throw<ArgumentException>();
        ((Action)(() => hasher.Hash(null!))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyKey()
    {
        ((Action)(() => new HmacSecretHasher([]))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_SixDigitCode_Works()
    {
        var hasher = HmacSecretHasher.CreateDev();

        var hash = hasher.Hash("000000");

        hasher.Verify("000000", hash).Should().BeTrue();
        hasher.Verify("000001", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_LegacySha256Hash_ReturnsFalse()
    {
        // HMAC hash should not verify against a SHA-256 hash of the same secret
        var hmacHasher = HmacSecretHasher.CreateDev();
        var shaHasher = new Sha256SecretHasher();
        var shaHash = shaHasher.Hash("123456");

        hmacHasher.Verify("123456", shaHash).Should().BeFalse();
    }
}
