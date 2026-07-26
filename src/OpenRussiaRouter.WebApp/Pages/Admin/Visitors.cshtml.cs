using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.SiteStats;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

/// <summary>
/// Admin-only visitor journey viewer. Reads <c>analytics.site_visits</c> via
/// <see cref="SiteVisitReader"/>. When <c>userId</c> is supplied, the reader resolves the
/// user's <c>distinct_id</c>, finds the matching <c>visitor_hash</c> from the most recent
/// signed-in visit, and returns the full history — anonymous views before sign-up plus the
/// signed-in ones afterwards — so the admin can reconstruct where a visitor came from and
/// what they browsed before registering.
/// </summary>
[Authorize(Roles = "Admin")]
public class VisitorsModel(SiteVisitReader reader, ILogger<VisitorsModel> logger) : PageModelBase
{
    private const int PageSize = 50;

    public IReadOnlyList<VisitHistoryEntry> Records { get; set; } = [];
    public bool AnalyticsEnabled { get; set; } = true;
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int TotalPages => TotalCount <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 0;
    public bool HasNextPage => PageNumber + 1 < TotalPages;

    public string? ResolvedVisitorHash { get; set; }
    public string? ResolvedDistinctId { get; set; }
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? VisitorHash { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; }

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(0, P);

        if (!reader.IsEnabled)
        {
            AnalyticsEnabled = false;
            return;
        }

        DateTimeOffset? fromUtc = ParseDate(From);
        DateTimeOffset? toUtc = ParseDate(To);

        var query = new SiteVisitQuery(
            UserId: UserId,
            VisitorHash: VisitorHash,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            PageNumber: PageNumber,
            PageSize: PageSize);

        try
        {
            var result = await reader.QueryAsync(query);
            Records = result.Items;
            TotalCount = result.TotalCount;
            ResolvedVisitorHash = result.ResolvedVisitorHash;
            ResolvedDistinctId = result.ResolvedDistinctId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load visitor history.");
            ErrorMessage = "Failed to load visitor history.";
        }
    }

    public bool HasActiveFilter() =>
        !string.IsNullOrWhiteSpace(UserId) || !string.IsNullOrWhiteSpace(VisitorHash) ||
        !string.IsNullOrWhiteSpace(From) || !string.IsNullOrWhiteSpace(To);

    public string BuildPageUrl(int page)
    {
        var parts = new List<string>();
        if (page > 0) parts.Add($"p={page}");
        if (!string.IsNullOrWhiteSpace(UserId))
            parts.Add($"userId={Uri.EscapeDataString(UserId!.Trim())}");
        if (!string.IsNullOrWhiteSpace(VisitorHash))
            parts.Add($"visitorHash={Uri.EscapeDataString(VisitorHash!.Trim())}");
        if (!string.IsNullOrWhiteSpace(From))
            parts.Add($"from={Uri.EscapeDataString(From!.Trim())}");
        if (!string.IsNullOrWhiteSpace(To))
            parts.Add($"to={Uri.EscapeDataString(To!.Trim())}");
        return "/admin/visitors" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    internal static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}