using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>
/// Builds the server → client payload consumed by the goal runner in the shared layout.
/// </summary>
/// <remarks>
/// The payload is emitted as a <c>&lt;script type="application/json"&gt;</c> island rather than
/// interpolated into executable code. That is what makes the runner injection-proof: the runner
/// itself is a static string with no server values in it, and everything variable — counter ids,
/// goal names, amounts — arrives as inert JSON.
/// <para>
/// Two details are load-bearing. The serializer keeps its <b>default encoder</b>, which escapes
/// <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>, so no value can close the surrounding script tag
/// (the same argument as for the JSON-LD blocks in <c>Services/Seo/StructuredData.cs</c>). And
/// <c>decimal</c> is written by <c>System.Text.Json</c> culture-invariantly — string
/// concatenation under <c>ru-RU</c> would have produced <c>1500,50</c> and a JS syntax error,
/// the same trap that already forced <c>Credits.TopUpAmount</c> to bind as a string.
/// </para>
/// </remarks>
public static class AnalyticsGoalScript
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Returns the JSON island body, or <c>null</c> when there is nothing to emit — no counter
    /// configured, or a counter but neither goals nor a user id to report.
    /// </summary>
    public static string? BuildPayloadJson(
        IEnumerable<AnalyticsGoal>? goals,
        string? googleTagId,
        string? yandexCounterId,
        string? userPseudonym,
        bool yandexTopUpCompletedClientEnabled = true)
    {
        var hasGoogle = !string.IsNullOrWhiteSpace(googleTagId);
        var hasYandex = !string.IsNullOrWhiteSpace(yandexCounterId);
        if (!hasGoogle && !hasYandex)
            return null;

        var items = goals?
            .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new AnalyticsGoalDto(g.Name, g.Value, g.Currency))
            .ToList() ?? [];

        // The runner is also what installs the [data-goal] click listener, so it has to be
        // emitted whenever a counter exists — even on a page with no server-side goals.
        return JsonSerializer.Serialize(
            new AnalyticsGoalPayload(
                hasYandex ? yandexCounterId : null,
                hasGoogle ? googleTagId : null,
                string.IsNullOrWhiteSpace(userPseudonym) ? null : userPseudonym,
                yandexTopUpCompletedClientEnabled,
                items),
            SerializerOptions);
    }

    private sealed record AnalyticsGoalPayload(
        [property: JsonPropertyName("ym")] string? YandexCounterId,
        [property: JsonPropertyName("ga")] string? GoogleTagId,
        [property: JsonPropertyName("uid")] string? UserPseudonym,
        [property: JsonPropertyName("ymTopUp")] bool YandexTopUpCompletedClientEnabled,
        [property: JsonPropertyName("goals")] IReadOnlyList<AnalyticsGoalDto> Goals);

    private sealed record AnalyticsGoalDto(
        [property: JsonPropertyName("n")] string Name,
        [property: JsonPropertyName("v")] decimal? Value,
        [property: JsonPropertyName("c")] string? Currency);
}
