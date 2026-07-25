using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class VisitorIdHasherTests
{
    private static VisitorIdHasher Hasher(string salt = "salt-1") =>
        new(new ProductAnalyticsOptions { IdentitySalt = salt });

    [Fact]
    public void ComputeVisitorHash_SameVisitor_ReturnsStableHash()
    {
        var first = Hasher();
        var second = Hasher();

        var firstHash = first.ComputeVisitorHash("203.0.113.7", "Mozilla/5.0");
        var secondHash = second.ComputeVisitorHash("203.0.113.7", "Mozilla/5.0");

        firstHash.Should().Be(secondHash);
    }

    [Fact]
    public void ComputeVisitorHash_DifferentIp_ProducesDifferentHash()
    {
        var hasher = Hasher();

        hasher.ComputeVisitorHash("203.0.113.7", "Mozilla/5.0")
            .Should().NotBe(hasher.ComputeVisitorHash("203.0.113.8", "Mozilla/5.0"));
    }

    [Fact]
    public void ComputeVisitorHash_SameIpDifferentUserAgent_ProducesDifferentHash()
    {
        var hasher = Hasher();

        hasher.ComputeVisitorHash("203.0.113.7", "Mozilla/5.0 (Windows)")
            .Should().NotBe(hasher.ComputeVisitorHash("203.0.113.7", "Mozilla/5.0 (Android)"));
    }

    [Fact]
    public void ComputeVisitorHash_DifferentSalts_ProduceDifferentHashes()
    {
        Hasher("salt-1").ComputeVisitorHash("203.0.113.7", "Mozilla/5.0")
            .Should().NotBe(Hasher("salt-2").ComputeVisitorHash("203.0.113.7", "Mozilla/5.0"));
    }

    [Fact]
    public void ComputeVisitorHash_DoesNotLeakRawIp()
    {
        const string ip = "203.0.113.7";

        var hash = Hasher().ComputeVisitorHash(ip, "Mozilla/5.0");

        hash.Should().NotContain(ip);
        hash.Should().NotContain("203");
    }

    [Fact]
    public void ComputeVisitorHash_MissingIpAndUserAgent_StillCountsVisitor()
    {
        var hasher = Hasher();

        var hash = hasher.ComputeVisitorHash(null, null);

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().Be(hasher.ComputeVisitorHash("   ", string.Empty));
    }

    [Fact]
    public void ComputeVisitorHash_UsesBase64UrlWithoutPadding()
    {
        var hash = Hasher().ComputeVisitorHash("203.0.113.7", "Mozilla/5.0");

        hash.Should().NotContain("+");
        hash.Should().NotContain("/");
        hash.Should().NotContain("=");
    }

    [Fact]
    public void Constructor_EmptySalt_ThrowsInvalidOperation()
    {
        var act = () => new VisitorIdHasher(new ProductAnalyticsOptions { IdentitySalt = string.Empty });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IdentitySalt*");
    }
}
