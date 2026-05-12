namespace OpenRussiaRouter.BuildingBlocks.Usage;

/// <summary>
/// Token counters reported by the router layer.
/// </summary>
/// <param name="InputTokens">Prompt/input tokens.</param>
/// <param name="OutputTokens">Completion/output tokens.</param>
/// <param name="CacheTokens">Cached tokens if reported by the provider/router.</param>
public readonly record struct TokenUsage(long InputTokens, long OutputTokens, long CacheTokens)
{
    /// <summary>
    /// Gets the total number of tokens across all tracked categories.
    /// </summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheTokens;

    /// <summary>
    /// Validates that every token counter is non-negative.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any token counter is negative.</exception>
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

