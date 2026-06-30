using System.Collections.Frozen;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Event name constants, per-event allow-lists and the global deny-list used by
/// <see cref="AnalyticsEventBuilder"/>. The primary privacy mechanism is the
/// allow-list (default-deny): a key is exported only if it appears in the allow-list
/// of the specific event. The deny-list is defense-in-depth — even if a caller smuggles
/// a sensitive key through the allow-list, the deny-list drops it before it leaves the
/// process.
/// </summary>
public static class AnalyticsEvents
{
    public const string UserVerified = "user verified";
    public const string ApiKeyCreated = "api key created";
    public const string ApiRequestCompleted = "api request completed";
    public const string PaymentCompleted = "payment completed";

    public const string SchemaVersionKey = "schema_version";
    public const string EnvironmentKey = "environment";

    /// <summary>Bumped whenever a property is added to or removed from the allow-list.</summary>
    public const string SchemaVersion = "2";

    /// <summary>
    /// Caller-supplied keys allowed per event. <see cref="SchemaVersionKey"/> and
    /// <see cref="EnvironmentKey"/> are added by the builder from configuration and are
    /// not part of this set.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> CallerSchemas =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [UserVerified] = new[] { "signup_method" }.ToFrozenSet(StringComparer.Ordinal),
            [ApiKeyCreated] = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal),
            [ApiRequestCompleted] = new[]
            {
                "model",
                "status_class",
                "input_tokens_bucket",
                "output_tokens_bucket",
                "latency_bucket",
                "endpoint",
                "stream",
            }.ToFrozenSet(StringComparer.Ordinal),
            [PaymentCompleted] = new[]
            {
                "provider",
                "currency",
                "amount_bucket",
            }.ToFrozenSet(StringComparer.Ordinal),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Keys that must never leave the process under any event. Compared case-insensitively
    /// so a future schema cannot bypass the defense with different casing.
    /// Covers identifiers and content categories called out by
    /// <c>posthog-safe-integration-plan.md</c>, section 5.
    /// </summary>
    public static readonly IReadOnlySet<string> DeniedKeys = new[]
    {
        "email",
        "name",
        "prompt",
        "response",
        "content",
        "message",
        "token",
        "secret",
        "api_key",
        "control_token",
        "raw_payload",
        "authorization",
        "cookie",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownEvent(string eventName) =>
        CallerSchemas.ContainsKey(eventName);

    public static bool TryGetAllowedCallerKeys(string eventName, out IReadOnlySet<string> allowedKeys) =>
        CallerSchemas.TryGetValue(eventName, out allowedKeys!);
}
