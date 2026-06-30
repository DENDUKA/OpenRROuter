namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Pure bucketing helpers for analytics payloads. Exact counts and amounts are replaced
/// by coarse labels before they leave the server — this is the structural guarantee that
/// precise token totals and payment amounts never reach the analytics sink.
/// </summary>
public static class AnalyticsBuckets
{
    /// <summary>
    /// Token count bucket:
    /// <c>0</c> for zero, <c>1-1K</c> for 1..1_000, <c>1K-10K</c> for 1_001..10_000,
    /// <c>10K-100K</c> for 10_001..100_000, <c>100K+</c> above.
    /// </summary>
    public static string TokenBucket(int tokens)
    {
        if (tokens <= 0) return "0";
        if (tokens <= 1_000) return "1-1K";
        if (tokens <= 10_000) return "1K-10K";
        if (tokens <= 100_000) return "10K-100K";
        return "100K+";
    }

    /// <summary>
    /// Payment amount bucket (in the payment's own currency units):
    /// <c>0</c> for zero, <c>1-1K</c> for 1..1_000, <c>1K-10K</c> for 1_001..10_000,
    /// <c>10K+</c> above.
    /// </summary>
    public static string AmountBucket(decimal amount)
    {
        if (amount <= 0m) return "0";
        if (amount <= 1_000m) return "1-1K";
        if (amount <= 10_000m) return "1K-10K";
        return "10K+";
    }

    /// <summary>
    /// Request latency bucket (milliseconds):
    /// <c>0-100ms</c>, <c>100-500ms</c>, <c>500ms-1s</c>, <c>1s-5s</c>, <c>5s+</c>.
    /// Negative inputs clamp to zero.
    /// </summary>
    public static string LatencyBucket(long latencyMs)
    {
        if (latencyMs < 0) latencyMs = 0;
        if (latencyMs <= 100) return "0-100ms";
        if (latencyMs <= 500) return "100-500ms";
        if (latencyMs <= 1_000) return "500ms-1s";
        if (latencyMs <= 5_000) return "1s-5s";
        return "5s+";
    }

    /// <summary>
    /// HTTP status class label: <c>2xx</c>, <c>3xx</c>, <c>4xx</c>, <c>5xx</c>, or
    /// <c>other</c> for anything outside the standard 2xx..5xx range.
    /// </summary>
    public static string StatusClass(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 and < 600 => "5xx",
        _ => "other",
    };
}