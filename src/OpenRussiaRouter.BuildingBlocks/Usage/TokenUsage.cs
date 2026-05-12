namespace OpenRussiaRouter.BuildingBlocks.Usage;

public readonly record struct TokenUsage(long InputTokens, long OutputTokens, long CacheTokens)
{

    public long TotalTokens => InputTokens + OutputTokens + CacheTokens;

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

