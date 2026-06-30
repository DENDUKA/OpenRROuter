using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class UserPseudonymizerTests
{
    [Fact]
    public void ComputeDistinctId_SameSaltAndUser_ReturnsStableId()
    {
        var first = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });
        var second = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });

        var firstId = first.ComputeDistinctId("user-123");
        var secondId = second.ComputeDistinctId("user-123");

        firstId.Should().Be(secondId);
    }

    [Fact]
    public void ComputeDistinctId_DifferentSalts_ProducesDifferentIds()
    {
        var first = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });
        var second = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-2" });

        var firstId = first.ComputeDistinctId("user-123");
        var secondId = second.ComputeDistinctId("user-123");

        firstId.Should().NotBe(secondId);
    }

    [Fact]
    public void ComputeDistinctId_DoesNotContainRawUserId()
    {
        var pseudonymizer = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });
        const string userId = "user-12345";

        var distinctId = pseudonymizer.ComputeDistinctId(userId);

        distinctId.Should().NotContain(userId);
    }

    [Fact]
    public void ComputeDistinctId_DifferentUsers_ProducesDifferentIds()
    {
        var pseudonymizer = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });

        pseudonymizer.ComputeDistinctId("user-1").Should().NotBe(pseudonymizer.ComputeDistinctId("user-2"));
    }

    [Fact]
    public void ComputeDistinctId_UsesBase64UrlWithoutPadding()
    {
        var pseudonymizer = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });

        var distinctId = pseudonymizer.ComputeDistinctId("user-1");

        distinctId.Should().NotContain("+");
        distinctId.Should().NotContain("/");
        distinctId.Should().NotContain("=");
    }

    [Fact]
    public void ComputeDistinctId_EmptyUserId_Throws()
    {
        var pseudonymizer = new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "salt-1" });

        var act = () => pseudonymizer.ComputeDistinctId(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptySalt_ThrowsInvalidOperation()
    {
        var act = () => new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = string.Empty });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IdentitySalt*");
    }

    [Fact]
    public void Constructor_WhitespaceSalt_ThrowsInvalidOperation()
    {
        var act = () => new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "   " });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IdentitySalt*");
    }
}
