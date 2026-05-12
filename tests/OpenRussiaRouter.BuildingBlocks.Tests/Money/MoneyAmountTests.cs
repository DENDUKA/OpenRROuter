using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Money;

public sealed class MoneyAmountTests
{
    [Fact]
    public void Addition_SameCurrency_ReturnsSum()
    {
        var left = MoneyAmount.Rub(10.25m);
        var right = MoneyAmount.Rub(5.75m);

        var result = left + right;

        Assert.Equal(MoneyAmount.Rub(16m), result);
    }

    [Fact]
    public void Subtraction_SameCurrency_ReturnsDifference()
    {
        var left = MoneyAmount.Rub(10.25m);
        var right = MoneyAmount.Rub(5m);

        var result = left - right;

        Assert.Equal(MoneyAmount.Rub(5.25m), result);
    }
}

