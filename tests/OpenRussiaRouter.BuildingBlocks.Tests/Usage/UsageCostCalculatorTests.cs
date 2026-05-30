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
    // Cache is a subset of input: cached part billed at cache rate, not input rate.
    [InlineData(1_000_000, 0, 1_000_000, 10, 20, 1, 1)]
    [InlineData(1_000_000, 0, 200_000, 10, 20, 1, 8.2)]
    // 400k non-cached input @10 + 250k output @20 + 100k cache @1.
    [InlineData(500_000, 250_000, 100_000, 10, 20, 1, 9.1)]
    // Cache greater than input is clamped to zero billable input (no negative charge).
    [InlineData(100_000, 0, 200_000, 10, 20, 1, 0.2)]
    public void Calculate_ValidUsage_ReturnsExpectedUsdCost(
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
            MoneyAmount.Usd(inputPrice),
            MoneyAmount.Usd(outputPrice),
            MoneyAmount.Usd(cachePrice));

        var result = _calculator.Calculate(usage, price);

        result.Currency.Should().Be(Currency.Usd);
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
            MoneyAmount.Usd(10),
            MoneyAmount.Usd(20),
            MoneyAmount.Usd(1));

        ((Action)(() => _calculator.Calculate(usage, price))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Calculate_NegativePrice_Throws()
    {
        var usage = new TokenUsage(1, 1, 1);
        var price = new ModelTokenPrice(
            MoneyAmount.Usd(-1),
            MoneyAmount.Usd(20),
            MoneyAmount.Usd(1));

        ((Action)(() => _calculator.Calculate(usage, price))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
