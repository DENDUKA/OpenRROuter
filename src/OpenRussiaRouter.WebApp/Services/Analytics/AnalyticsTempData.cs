using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>
/// Carries goals across a POST-redirect-GET, and a pending top-up across the round trip to the
/// payment provider.
/// </summary>
/// <remarks>
/// Riding on <c>TempData</c> is what makes goals fire exactly once: the default
/// <c>CookieTempDataProvider</c> keeps the value in a Data-Protection-encrypted cookie, which
/// survives the top-level GET back from the provider (SameSite=Lax allows it), and
/// <c>TempDataDictionary</c> drops a key the moment it is read. Unread keys are carried forward
/// untouched, so intermediate navigations cannot consume a pending goal.
/// <para>
/// Everything is stored as a JSON <b>string</b> on purpose — <c>CookieTempDataProvider</c> can
/// only round-trip primitives and throws while saving anything else.
/// </para>
/// </remarks>
public static class AnalyticsTempData
{
    private const string GoalsKey = "orr.analytics.goals";
    private const string PendingTopUpKey = "orr.analytics.topup";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Queues goals for the next render. Repeated calls accumulate.</summary>
    public static void Enqueue(ITempDataDictionary tempData, params AnalyticsGoal[] goals)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        if (goals is null || goals.Length == 0)
            return;

        // Peek, not index: reading through the indexer would mark the key consumed and the
        // goals already queued by an earlier call would vanish on save.
        var pending = Deserialize(tempData.Peek(GoalsKey) as string).ToList();
        pending.AddRange(goals.Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new GoalDto(g.Name, g.Value, g.Currency)));

        tempData[GoalsKey] = JsonSerializer.Serialize(pending, SerializerOptions);
    }

    /// <summary>Takes every queued goal, clearing the queue.</summary>
    public static IReadOnlyList<AnalyticsGoal> Dequeue(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);

        // Reading through the indexer is what stops the value carrying into the next request;
        // Remove additionally clears it for this one, so a handler that runs twice cannot report
        // the same conversion twice.
        var raw = tempData[GoalsKey] as string;
        tempData.Remove(GoalsKey);

        return Deserialize(raw)
            .Select(d => new AnalyticsGoal(d.Name, d.Value, d.Currency))
            .ToList();
    }

    /// <summary>
    /// Remembers what the user is about to be charged, so the goal fired on the way back can
    /// report a value. Stores what the provider will actually take, not what was typed.
    /// </summary>
    public static void SetPendingTopUp(ITempDataDictionary tempData, decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        if (amount <= 0 || string.IsNullOrWhiteSpace(currency))
            return;

        tempData[PendingTopUpKey] = JsonSerializer.Serialize(
            new PendingTopUpDto(amount, currency), SerializerOptions);
    }

    /// <summary>
    /// Takes the pending top-up, or <c>null</c> when the cookie did not make it back (cleared
    /// cookies, a different browser, a provider that returns by POST). Callers must still count
    /// the conversion in that case — only the value is lost, and losing it understates revenue
    /// rather than inventing it.
    /// </summary>
    public static (decimal Amount, string Currency)? TakePendingTopUp(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);

        var stored = tempData[PendingTopUpKey] as string;
        tempData.Remove(PendingTopUpKey);

        if (string.IsNullOrWhiteSpace(stored))
            return null;

        try
        {
            var dto = JsonSerializer.Deserialize<PendingTopUpDto>(stored, SerializerOptions);
            if (dto is null || dto.Amount <= 0 || string.IsNullOrWhiteSpace(dto.Currency))
                return null;
            return (dto.Amount, dto.Currency);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<GoalDto> Deserialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<GoalDto>>(raw, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            // A malformed cookie must not take a page down over analytics.
            return [];
        }
    }

    private sealed record GoalDto(
        [property: JsonPropertyName("n")] string Name,
        [property: JsonPropertyName("v")] decimal? Value,
        [property: JsonPropertyName("c")] string? Currency);

    private sealed record PendingTopUpDto(
        [property: JsonPropertyName("v")] decimal Amount,
        [property: JsonPropertyName("c")] string Currency);
}
