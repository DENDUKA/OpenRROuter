using System.Collections.Frozen;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Built analytics event ready to be enqueued by a concrete <see cref="IProductAnalytics"/>
/// implementation. <see cref="Props"/> is a closed dictionary — no caller-controlled
/// keys can sneak in past the builder.
/// </summary>
public sealed record AnalyticsEvent(
    string EventName,
    string DistinctId,
    IReadOnlyDictionary<string, object> Props);

/// <summary>
/// Builds <see cref="AnalyticsEvent"/> instances from typed payloads. Applies three
/// filters in order:
/// <list type="number">
///   <item><description>default-deny allow-list (per-event caller-supplied keys),</description></item>
///   <item><description>defense-in-depth deny-list (forbidden keys stripped on the way out),</description></item>
///   <item><description>forced injection of <c>schema_version</c> and <c>environment</c>.</description></item>
/// </list>
/// </summary>
public sealed class AnalyticsEventBuilder
{
    private readonly UserPseudonymizer _pseudonymizer;
    private readonly string _environment;

    public AnalyticsEventBuilder(UserPseudonymizer pseudonymizer, string? environment)
    {
        ArgumentNullException.ThrowIfNull(pseudonymizer);
        _pseudonymizer = pseudonymizer;
        _environment = environment ?? string.Empty;
    }

    public AnalyticsEvent BuildUserVerified(string userId, string signupMethod)
    {
        var raw = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["signup_method"] = Preconditions.RequireValue(signupMethod, nameof(signupMethod)),
        };
        return Build(AnalyticsEvents.UserVerified, userId, raw);
    }

    public AnalyticsEvent BuildApiKeyCreated(string userId)
    {
        return Build(AnalyticsEvents.ApiKeyCreated, userId, new Dictionary<string, object>(StringComparer.Ordinal));
    }

    public AnalyticsEvent BuildApiRequestCompleted(string userId, ApiRequestAnalytics data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var raw = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = data.Model,
            ["status_class"] = AnalyticsBuckets.StatusClass(data.StatusCode),
            ["input_tokens_bucket"] = AnalyticsBuckets.TokenBucket(data.InputTokens),
            ["output_tokens_bucket"] = AnalyticsBuckets.TokenBucket(data.OutputTokens),
            ["latency_bucket"] = AnalyticsBuckets.LatencyBucket(data.LatencyMs),
        };

        if (data.Endpoint.HasValue)
        {
            raw["endpoint"] = data.Endpoint.Value switch
            {
                ApiEndpoint.ChatCompletions => "chat_completions",
                ApiEndpoint.Tts => "tts",
                _ => "unknown",
            };
        }

        if (data.Stream.HasValue)
        {
            raw["stream"] = data.Stream.Value;
        }

        return Build(AnalyticsEvents.ApiRequestCompleted, userId, raw);
    }

    public AnalyticsEvent BuildPaymentCompleted(string userId, PaymentAnalytics data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var raw = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["provider"] = Preconditions.RequireValue(data.Provider, nameof(data.Provider)),
            ["currency"] = Preconditions.RequireValue(data.Currency, nameof(data.Currency)),
            ["amount_bucket"] = AnalyticsBuckets.AmountBucket(data.Amount),
        };
        return Build(AnalyticsEvents.PaymentCompleted, userId, raw);
    }

    /// <summary>
    /// Generic builder used by the typed methods above and exposed publicly so callers
    /// (and tests) can verify the filter behaviour with arbitrary input keys.
    /// </summary>
    public AnalyticsEvent Build(
        string eventName,
        string userId,
        IReadOnlyDictionary<string, object> rawProperties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rawProperties);

        if (!AnalyticsEvents.IsKnownEvent(eventName))
        {
            throw new InvalidOperationException($"Unknown analytics event '{eventName}'.");
        }

        AnalyticsEvents.TryGetAllowedCallerKeys(eventName, out var allowedKeys);

        // Default-deny: only keys from the allow-list survive. Capacity reserves room
        // for the two builder-injected keys (schema_version, environment).
        var props = new Dictionary<string, object>(allowedKeys.Count + 2, StringComparer.Ordinal);
        foreach (var key in allowedKeys)
        {
            if (rawProperties.TryGetValue(key, out var value) && value is not null)
            {
                props[key] = value;
            }
        }

        props[AnalyticsEvents.SchemaVersionKey] = AnalyticsEvents.SchemaVersion;
        props[AnalyticsEvents.EnvironmentKey] = _environment;

        // Defense-in-depth: even if a forbidden key somehow made it into the dictionary,
        // it must not leave the process. In Debug builds we surface the invariant
        // violation loudly; in Release we silently drop the key to keep analytics
        // fail-open (a leaked forbidden key must never break business flow).
        var deniedKeys = props.Keys.Where(AnalyticsEvents.DeniedKeys.Contains).ToArray();
#if DEBUG
        if (deniedKeys.Length > 0)
        {
            throw new InvalidOperationException(
                $"Analytics invariant violation: forbidden key '{deniedKeys[0]}' leaked into event '{eventName}'.");
        }
#else
        foreach (var key in deniedKeys)
        {
            props.Remove(key);
        }
#endif

        var distinctId = _pseudonymizer.ComputeDistinctId(userId);
        return new AnalyticsEvent(
            eventName,
            distinctId,
            props.ToFrozenDictionary(StringComparer.Ordinal));
    }

}
