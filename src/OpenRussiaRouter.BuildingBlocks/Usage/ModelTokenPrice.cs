using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

/// <summary>
/// Per-million-token price for a model in one currency.
/// </summary>
/// <param name="InputPerMillion">Input token price per 1M tokens.</param>
/// <param name="OutputPerMillion">Output token price per 1M tokens.</param>
/// <param name="CachePerMillion">Cache token price per 1M tokens.</param>
public sealed record ModelTokenPrice(
    MoneyAmount InputPerMillion,
    MoneyAmount OutputPerMillion,
    MoneyAmount CachePerMillion)
{
    /// <summary>
    /// Validates that all price parts use the same currency and are non-negative.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a price is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when currencies differ.</exception>
    public void EnsureValid()
    {
        EnsureNonNegative(InputPerMillion, nameof(InputPerMillion));
        EnsureNonNegative(OutputPerMillion, nameof(OutputPerMillion));
        EnsureNonNegative(CachePerMillion, nameof(CachePerMillion));

        if (InputPerMillion.Currency != OutputPerMillion.Currency ||
            InputPerMillion.Currency != CachePerMillion.Currency)
        {
            throw new InvalidOperationException("All token prices must use the same currency.");
        }
    }

    private static void EnsureNonNegative(MoneyAmount amount, string name)
    {
        if (amount.Amount < 0)
        {
            throw new ArgumentOutOfRangeException(name, "Token price cannot be negative.");
        }
    }
}

