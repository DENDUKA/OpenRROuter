using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

public sealed record ModelTokenPrice(
    MoneyAmount InputPerMillion,
    MoneyAmount OutputPerMillion,
    MoneyAmount CachePerMillion)
{

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

