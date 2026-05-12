using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

/// <summary>
/// Calculates request cost from token usage and per-million-token model prices.
/// </summary>
public static class UsageCostCalculator
{
    private const decimal OneMillion = 1_000_000m;

    /// <summary>
    /// Calculates the total cost for one model usage event.
    /// </summary>
    /// <param name="usage">Token counters reported by 9Router.</param>
    /// <param name="price">Per-million-token model price.</param>
    /// <returns>Total request cost.</returns>
    public static MoneyAmount Calculate(TokenUsage usage, ModelTokenPrice price)
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

