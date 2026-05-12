using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

/// <summary>
/// Calculates request cost from token usage and per-million-token model prices.
/// </summary>
public interface IUsageCostCalculator
{
    /// <summary>
    /// Calculates the total cost for one model usage event.
    /// </summary>
    /// <param name="usage">Token counters reported by 9Router.</param>
    /// <param name="price">Per-million-token model price.</param>
    /// <returns>Total request cost.</returns>
    MoneyAmount Calculate(TokenUsage usage, ModelTokenPrice price);
}
