using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Money;

public sealed class MoneyAmountTests
{
    [Fact]
    public void Addition_SameCurrency_ReturnsSum()
    {
        var left = MoneyAmount.Usd(10.25m);
        var right = MoneyAmount.Usd(5.75m);

        var result = left + right;

        result.Should().Be(MoneyAmount.Usd(16m));
    }

    [Fact]
    public void Subtraction_SameCurrency_ReturnsDifference()
    {
        var left = MoneyAmount.Usd(10.25m);
        var right = MoneyAmount.Usd(5m);

        var result = left - right;

        result.Should().Be(MoneyAmount.Usd(5.25m));
    }

    [Fact]
    public void Addition_ZeroAmounts_ReturnsZero()
    {
        var left = MoneyAmount.Usd(0m);
        var right = MoneyAmount.Usd(0m);

        var result = left + right;

        result.Should().Be(MoneyAmount.Usd(0m));
    }

    [Fact]
    public void Subtraction_EqualAmounts_ReturnsZero()
    {
        var left = MoneyAmount.Usd(42m);
        var right = MoneyAmount.Usd(42m);

        var result = left - right;

        result.Should().Be(MoneyAmount.Usd(0m));
    }

    [Fact]
    public void Equality_SameValues_ReturnsTrue()
    {
        var a = MoneyAmount.Usd(10m);
        var b = MoneyAmount.Usd(10m);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsRecordRepresentation()
    {
        var amount = MoneyAmount.Usd(42m);

        amount.ToString().Should().Contain("42");
        amount.ToString().Should().Contain("Usd");
    }
}
