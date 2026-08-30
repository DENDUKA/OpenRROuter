namespace OpenRussiaRouter.BillingService.Application.Services;

public static class ReasoningEffort
{
    private static readonly HashSet<string> CanonicalSet = new(StringComparer.Ordinal)
    {
        "none",
        "minimal",
        "low",
        "medium",
        "high",
        "xhigh",
        "max",
        "ultra",
        "thinking"
    };

    public static IReadOnlyList<string> Canonical { get; } = ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra", "thinking"];

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Normalize(value);

    public static bool IsCanonical(string value) =>
        !string.IsNullOrWhiteSpace(value) && CanonicalSet.Contains(Normalize(value));

    public static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string JoinCsv(IReadOnlyList<string>? values)
    {
        var normalized = NormalizeList(values);
        return normalized.Count == 0 ? string.Empty : string.Join(',', normalized);
    }

    public static IReadOnlyList<string>? SplitCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }
}
