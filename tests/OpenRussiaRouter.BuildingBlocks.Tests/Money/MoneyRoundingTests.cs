using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Money;

public sealed class MoneyRoundingTests
{
    [Fact]
    public void Round_TrimsToSixDecimalPlaces()
    {
        MoneyRounding.Round(0.1234567m).Should().Be(0.123457m);
        MoneyRounding.Round(1m / 3m).Should().Be(0.333333m);
    }

    [Fact]
    public void Round_AppliesBankersRoundingAtTheSixthDigit()
    {
        // 0.0123445: 7th fractional digit is exactly 5, 6th is 4 (even) → round to even (down).
        // away-from-zero would round up to 0.012345 — this is the policy-divergence case for T-033.
        MoneyRounding.Round(0.0123445m).Should().Be(0.012344m);
        // 0.0123455: 7th fractional digit is exactly 5, 6th is 5 (odd) → round to even (up).
        MoneyRounding.Round(0.0123455m).Should().Be(0.012346m);
    }

    [Fact]
    public void Round_PreservesCurrency()
    {
        var rounded = MoneyRounding.Round(MoneyAmount.Usd(0.0123445m));

        rounded.Should().Be(MoneyAmount.Usd(0.012344m));
        rounded.Currency.Should().Be(Currency.Usd);
    }
}