using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Usage;

public sealed class UsageCostCalculatorTests
{
    private readonly UsageCostCalculator _calculator = new();

    [Theory]
    [InlineData(1_000_000, 0, 0, 10, 20, 1, 10)]
    [InlineData(0, 1_000_000, 0, 10, 20, 1, 20)]
    [InlineData(0, 0, 1_000_000, 10, 20, 1, 1)]
    [InlineData(500_000, 250_000, 100_000, 10, 20, 1, 10.1)]
    public void Calculate_ValidUsage_ReturnsExpectedRubCost(
        long inputTokens,
        long outputTokens,
        long cacheTokens,
        decimal inputPrice,
        decimal outputPrice,
        decimal cachePrice,
        decimal expected)
    {
        var usage = new TokenUsage(inputTokens, outputTokens, cacheTokens);
        var price = new ModelTokenPrice(
            MoneyAmount.Rub(inputPrice),
            MoneyAmount.Rub(outputPrice),
            MoneyAmount.Rub(cachePrice));

        var result = _calculator.Calculate(usage, price);

        result.Currency.Should().Be(Currency.Rub);
        result.Amount.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Calculate_NegativeTokenCount_Throws(long inputTokens, long outputTokens, long cacheTokens)
    {
        var usage = new TokenUsage(inputTokens, outputTokens, cacheTokens);
        var price = new ModelTokenPrice(
            MoneyAmount.Rub(10),
            MoneyAmount.Rub(20),
            MoneyAmount.Rub(1));

        ((Action)(() => _calculator.Calculate(usage, price))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Calculate_NegativePrice_Throws()
    {
        var usage = new TokenUsage(1, 1, 1);
        var price = new ModelTokenPrice(
            MoneyAmount.Rub(-1),
            MoneyAmount.Rub(20),
            MoneyAmount.Rub(1));

        ((Action)(() => _calculator.Calculate(usage, price))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
