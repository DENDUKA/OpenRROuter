using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class RequestsModel(BillingProvider billingProvider, ControlPlaneProvider controlPlaneProvider, ILogger<RequestsModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingProvider = billingProvider;
    private readonly ControlPlaneProvider _controlPlaneProvider = controlPlaneProvider;
    public IReadOnlyList<AdminRequestInfo> Records { get; set; } = [];
    public Dictionary<string, string> NodeNames { get; set; } = [];
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<string> ModelFilters { get; set; } = [];
    public IReadOnlyList<string> ProviderFilters { get; set; } = [];
    public IReadOnlyList<string> ApiKeyFilters { get; set; } = [];
    public IReadOnlyList<string> NodeFilters { get; set; } = [];
    public IReadOnlyList<string> AccountFilters { get; set; } = [];
    public string? FromUtc { get; set; }
    public string? ToUtc { get; set; }
    public string? Cursor { get; set; }

    public long SummaryTotalRequests { get; set; }
    public long SummaryInputTokens { get; set; }
    public long SummaryOutputTokens { get; set; }
    public long SummaryCacheTokens { get; set; }
    public decimal SummaryCostUsd { get; set; }
    public bool ShowSummary { get; set; }

    /// <summary>Distinct values available for the multi-select filter dropdowns,
    /// built from the current page of records merged with the currently active
    /// filters (so active filters stay visible even when not on this page).</summary>
    public IReadOnlyCollection<FilterOption> ModelOptions { get; private set; } = [];
    public IReadOnlyCollection<FilterOption> ProviderOptions { get; private set; } = [];
    public IReadOnlyCollection<FilterOption> ApiKeyOptions { get; private set; } = [];
    public IReadOnlyCollection<FilterOption> NodeOptions { get; private set; } = [];
    public IReadOnlyCollection<FilterOption> AccountOptions { get; private set; } = [];

    public sealed class FilterOption
    {
        public string Value { get; init; } = "";
        public string Display { get; init; } = "";
        public bool Active { get; init; }
    }

    public async Task OnGetAsync(
        string[]? model = null,
        string[]? provider = null,
        string[]? apiKey = null,
        string[]? node = null,
        string[]? account = null,
        string? from = null,
        string? to = null,
        string? cursor = null)
    {
        ModelFilters = ParseFilterArray(model);
        ProviderFilters = ParseFilterArray(provider);
        ApiKeyFilters = ParseFilterArray(apiKey);
        NodeFilters = ParseFilterArray(node);
        AccountFilters = ParseFilterArray(account);
        FromUtc = from;
        ToUtc = to;
        Cursor = cursor;

        DateTimeOffset? fromParsed = DateTimeOffset.TryParse(from, out var fp) ? fp : null;
        DateTimeOffset? toParsed = DateTimeOffset.TryParse(to, out var tp) ? tp : null;

        try
        {
            var (billingTask, nodesTask) = (
                _billingProvider.ListAdminRequestsAsync(
                    afterCursor: cursor,
                    pageSize: 50,
                    modelFilters: ModelFilters,
                    providerFilters: ProviderFilters,
                    apiKeyFilters: ApiKeyFilters,
                    nodeFilters: NodeFilters,
                    accountFilters: AccountFilters,
                    fromUtc: fromParsed,
                    toUtc: toParsed),
                _controlPlaneProvider.ListRouterNodesAsync());

            await Task.WhenAll(billingTask, nodesTask);

            var (items, hasMore, nextCursor) = await billingTask;
            Records = items;
            HasMore = hasMore;
            NextCursor = nextCursor;

            NodeNames = (await nodesTask)
                .ToDictionary(n => n.NodeId, n => n.Name, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load admin requests.");
            ErrorMessage = "Failed to load requests.";
        }

        ModelOptions = BuildOptions(
            Records.Select(r => r.ModelId),
            ModelFilters,
            v => v);
        ProviderOptions = BuildOptions(
            Records.Select(r => r.ProviderName).Where(v => !string.IsNullOrEmpty(v))!,
            ProviderFilters,
            v => v);
        ApiKeyOptions = BuildOptions(
            Records.Select(r => r.ApiKeyPrefix).Where(v => !string.IsNullOrEmpty(v))!,
            ApiKeyFilters,
            v => v + "…");
        NodeOptions = BuildOptions(
            Records.Select(r => r.RouterNodeId),
            NodeFilters,
            v => NodeNames.TryGetValue(v, out var name) ? name : v);
        AccountOptions = BuildOptions(
            Records.Select(r => r.ProviderAccount).Where(v => !string.IsNullOrEmpty(v))!,
            AccountFilters,
            v => v);
    }

    public async Task<IActionResult> OnGetCalculateAsync(
        string[]? model = null,
        string[]? provider = null,
        string[]? apiKey = null,
        string[]? node = null,
        string[]? account = null,
        string? from = null,
        string? to = null)
    {
        var modelFilters = ParseFilterArray(model);
        var providerFilters = ParseFilterArray(provider);
        var apiKeyFilters = ParseFilterArray(apiKey);
        var nodeFilters = ParseFilterArray(node);
        var accountFilters = ParseFilterArray(account);

        DateTimeOffset? fromParsed = DateTimeOffset.TryParse(from, out var fp) ? fp : null;
        DateTimeOffset? toParsed = DateTimeOffset.TryParse(to, out var tp) ? tp : null;

        try
        {
            var summary = await _billingProvider.GetAdminRequestsSummaryAsync(
                modelFilters: modelFilters,
                providerFilters: providerFilters,
                apiKeyFilters: apiKeyFilters,
                nodeFilters: nodeFilters,
                accountFilters: accountFilters,
                fromUtc: fromParsed,
                toUtc: toParsed);

            return new JsonResult(new
            {
                totalRequests = summary.TotalRequests,
                inputTokens = summary.InputTokens,
                outputTokens = summary.OutputTokens,
                cacheTokens = summary.CacheTokens,
                costUsd = summary.CostUsd
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to calculate admin requests summary.");
            return new JsonResult(new { error = "Failed to calculate summary." }) { StatusCode = 500 };
        }
    }

    public string GetNodeName(string nodeId) =>
        NodeNames.TryGetValue(nodeId, out var name) ? name : nodeId;

    public (string cssClass, string label) GetStatusBadge(string status) => status switch
    {
        "completed" => ("badge-success", "completed"),
        "failed" => ("badge-danger", "failed"),
        "usage_missing" => ("badge-warning", "usage_missing"),
        _ => ("badge-secondary", status)
    };

    public bool HasActiveFilters() =>
        ModelFilters.Count > 0 || ProviderFilters.Count > 0 || ApiKeyFilters.Count > 0 ||
        NodeFilters.Count > 0 || AccountFilters.Count > 0 || FromUtc != null || ToUtc != null;

    public string BuildFilterUrl(string? overrideCursor = null) =>
        BuildUrl(ModelFilters, ProviderFilters, ApiKeyFilters, NodeFilters, AccountFilters, FromUtc, ToUtc, overrideCursor);

    public string BuildRemoveFilterUrl(string filterType, string filterValue)
    {
        return filterType switch
        {
            "model" => BuildUrl(ModelFilters.Where(f => f != filterValue).ToList(), ProviderFilters, ApiKeyFilters, NodeFilters, AccountFilters, FromUtc, ToUtc, null),
            "provider" => BuildUrl(ModelFilters, ProviderFilters.Where(f => f != filterValue).ToList(), ApiKeyFilters, NodeFilters, AccountFilters, FromUtc, ToUtc, null),
            "apiKey" => BuildUrl(ModelFilters, ProviderFilters, ApiKeyFilters.Where(f => f != filterValue).ToList(), NodeFilters, AccountFilters, FromUtc, ToUtc, null),
            "node" => BuildUrl(ModelFilters, ProviderFilters, ApiKeyFilters, NodeFilters.Where(f => f != filterValue).ToList(), AccountFilters, FromUtc, ToUtc, null),
            "account" => BuildUrl(ModelFilters, ProviderFilters, ApiKeyFilters, NodeFilters, AccountFilters.Where(f => f != filterValue).ToList(), FromUtc, ToUtc, null),
            _ => BuildUrl(ModelFilters, ProviderFilters, ApiKeyFilters, NodeFilters, AccountFilters, FromUtc, ToUtc, null)
        };
    }

    private static string BuildUrl(
        IReadOnlyList<string> models, IReadOnlyList<string> providers, IReadOnlyList<string> apiKeys,
        IReadOnlyList<string> nodes, IReadOnlyList<string> accounts,
        string? from, string? to, string? cursor)
    {
        var parts = new List<string>();
        foreach (var m in models) parts.Add($"model={Uri.EscapeDataString(m)}");
        foreach (var p in providers) parts.Add($"provider={Uri.EscapeDataString(p)}");
        foreach (var k in apiKeys) parts.Add($"apiKey={Uri.EscapeDataString(k)}");
        foreach (var n in nodes) parts.Add($"node={Uri.EscapeDataString(n)}");
        foreach (var a in accounts) parts.Add($"account={Uri.EscapeDataString(a)}");
        if (!string.IsNullOrEmpty(from)) parts.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrEmpty(to)) parts.Add($"to={Uri.EscapeDataString(to)}");
        if (!string.IsNullOrEmpty(cursor)) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return "/admin/requests" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    private static IReadOnlyList<string> ParseFilterArray(string[]? values)
    {
        if (values == null) return [];
        return values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyCollection<FilterOption> BuildOptions(
        IEnumerable<string> fromRecords,
        IReadOnlyList<string> activeFilters,
        Func<string, string> display)
    {
        var activeSet = new HashSet<string>(activeFilters, StringComparer.OrdinalIgnoreCase);
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in fromRecords)
        {
            if (!string.IsNullOrEmpty(v)) values.Add(v);
        }
        foreach (var v in activeFilters)
        {
            if (!string.IsNullOrEmpty(v)) values.Add(v);
        }
        return values
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new FilterOption
            {
                Value = v,
                Display = display(v),
                Active = activeSet.Contains(v)
            })
            .ToArray();
    }
}
