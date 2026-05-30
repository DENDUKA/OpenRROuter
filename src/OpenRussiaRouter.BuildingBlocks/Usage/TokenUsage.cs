namespace OpenRussiaRouter.BuildingBlocks.Usage;

/// <summary>
/// Token counts for a single request. Invariant: <see cref="InputTokens"/> includes
/// <see cref="CacheTokens"/> (cached tokens are a subset of the prompt). Billing prices
/// the cached portion at the cache rate and the remainder at the input rate
/// (see <see cref="UsageCostCalculator"/>).
/// </summary>
public readonly record struct TokenUsage(long InputTokens, long OutputTokens, long CacheTokens)
{

    // CacheTokens are a subset of InputTokens, so they are not added again here.
    public long TotalTokens => InputTokens + OutputTokens;

    public void EnsureValid()
    {
        if (InputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InputTokens), "Input tokens cannot be negative.");
        }

        if (OutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputTokens), "Output tokens cannot be negative.");
        }

        if (CacheTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CacheTokens), "Cache tokens cannot be negative.");
        }
    }
}

