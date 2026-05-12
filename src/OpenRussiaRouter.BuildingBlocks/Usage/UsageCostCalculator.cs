using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

/// <summary>
/// Calculates request cost from token usage and per-million-token model prices.
/// </summary>
public sealed class UsageCostCalculator : IUsageCostCalculator
{
    private const decimal OneMillion = 1_000_000m;

    /// <inheritdoc />
    public MoneyAmount Calculate(TokenUsage usage, ModelTokenPrice price)
    {
        usage.EnsureValid();
        price.EnsureValid();

        var amount =
            usage.InputTokens * price.InputPerMillion.Amount / OneMillion +
            usage.OutputTokens * price.OutputPerMillion.Amount / OneMillion +
            usage.CacheTokens * price.CachePerMillion.Amount / OneMillion;

        return new MoneyAmount(amount, price.InputPerMillion.Currency);
    }
}

