using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Usage;

public sealed class TokenUsageTests
{
    [Fact]
    public void TotalTokens_SumsInputAndOutput()
    {
        var usage = new TokenUsage(100, 50, 10);

        usage.TotalTokens.Should().Be(150);
    }

    [Fact]
    public void TotalTokens_DoesNotDoubleCountCache()
    {
        var usage = new TokenUsage(100, 50, 30);

        usage.TotalTokens.Should().Be(150);
    }

    [Fact]
    public void EnsureValid_NegativeInputTokens_Throws()
    {
        var usage = new TokenUsage(-1, 0, 0);

        var act = () => usage.EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("InputTokens");
    }

    [Fact]
    public void EnsureValid_NegativeOutputTokens_Throws()
    {
        var usage = new TokenUsage(0, -1, 0);

        var act = () => usage.EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("OutputTokens");
    }

    [Fact]
    public void EnsureValid_NegativeCacheTokens_Throws()
    {
        var usage = new TokenUsage(0, 0, -1);

        var act = () => usage.EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("CacheTokens");
    }

    [Fact]
    public void EnsureValid_AllZeros_DoesNotThrow()
    {
        var usage = new TokenUsage(0, 0, 0);

        var act = () => usage.EnsureValid();

        act.Should().NotThrow();
    }
}
