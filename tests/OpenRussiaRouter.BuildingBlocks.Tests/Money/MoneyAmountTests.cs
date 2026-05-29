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
}
