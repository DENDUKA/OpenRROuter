using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Usage;

public sealed class ModelTokenPriceTests
{
    [Fact]
    public void EnsureValid_AllZeros_DoesNotThrow()
    {
        var price = new ModelTokenPrice(
            MoneyAmount.Usd(0),
            MoneyAmount.Usd(0),
            MoneyAmount.Usd(0));

        var act = () => price.EnsureValid();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_PositivePrices_DoesNotThrow()
    {
        var price = new ModelTokenPrice(
            MoneyAmount.Usd(10),
            MoneyAmount.Usd(20),
            MoneyAmount.Usd(5));

        var act = () => price.EnsureValid();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_NegativeInputPrice_ThrowsArgumentOutOfRangeException()
    {
        var price = new ModelTokenPrice(
            MoneyAmount.Usd(-1),
            MoneyAmount.Usd(20),
            MoneyAmount.Usd(5));

        var act = () => price.EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("InputPerMillion");
    }

    [Fact]
    public void EnsureValid_NegativeOutputPrice_ThrowsArgumentOutOfRangeException()
    {
        var price = new ModelTokenPrice(
            MoneyAmount.Usd(10),
            MoneyAmount.Usd(-1),
            MoneyAmount.Usd(5));

        var act = () => price.EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("OutputPerMillion");
    }

    [Fact]
    public void EnsureValid_NegativeCachePrice_ThrowsArgumentOutOfRangeException()
    {
        var price = new ModelTokenPrice(
            MoneyAmount.Usd(10),
            MoneyAmount.Usd(20),
            MoneyAmount.Usd(-1));

        var act = () => price.EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("CachePerMillion");
    }
}
