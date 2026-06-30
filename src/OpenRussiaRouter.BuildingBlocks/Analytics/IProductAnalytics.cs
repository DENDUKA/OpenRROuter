namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// API endpoint type for analytics.
/// </summary>
public enum ApiEndpoint
{
    /// <summary>
    /// Chat completions endpoint (/v1/chat/completions).
    /// </summary>
    ChatCompletions,

    /// <summary>
    /// Text-to-speech endpoint (/v1/audio/speech).
    /// </summary>
    Tts,
}

/// <summary>
/// Raw, typed payload for a single completed API request. Bucketing of token counts,
/// latency and status code is performed by <see cref="AnalyticsEventBuilder"/>; the
/// values on this record are intentionally precise so the bucket boundaries stay
/// testable and the calling code stays explicit about what it measured.
/// </summary>
/// <param name="DedupKey">Opaque business identifier (usage record id) used to derive a
/// deterministic event uuid for deduplication in the downstream sink.</param>
public sealed record ApiRequestAnalytics(
    string Model,
    int StatusCode,
    int InputTokens,
    int OutputTokens,
    long LatencyMs,
    string DedupKey,
    ApiEndpoint? Endpoint = null,
    bool? Stream = null);

/// <summary>
/// Raw, typed payload for a completed payment. Exact amount is replaced with a bucket
/// label by <see cref="AnalyticsEventBuilder"/>; only <paramref name="Provider"/>,
/// <paramref name="Currency"/> and the amount bucket are exported.
/// </summary>
/// <param name="DedupKey">Opaque business identifier (payment id) used to derive a
/// deterministic event uuid for deduplication in the downstream sink.</param>
public sealed record PaymentAnalytics(
    string Provider,
    string Currency,
    decimal Amount,
    string DedupKey);

/// <summary>
/// Provider-agnostic server-side product analytics surface. Methods are synchronous
/// (<c>void</c>): implementations only stage/queue an event and must never throw.
/// </summary>
public interface IProductAnalytics
{
    void UserVerified(string userId, string signupMethod);

    void ApiKeyCreated(string userId);

    void ApiRequestCompleted(string userId, ApiRequestAnalytics data);

    void PaymentCompleted(string userId, PaymentAnalytics data);
}
