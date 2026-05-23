using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class RequestsModel(BillingClient billingClient, ControlPlaneClient controlPlaneClient) : PageModelBase
{
    public IReadOnlyList<AdminRequestInfo> Records { get; set; } = [];
    public Dictionary<string, string> NodeNames { get; set; } = [];
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
    public string? ErrorMessage { get; set; }

    public string? ModelFilter { get; set; }
    public string? ProviderFilter { get; set; }
    public string? FromUtc { get; set; }
    public string? ToUtc { get; set; }
    public string? Cursor { get; set; }

    public async Task OnGetAsync(
        string? model = null,
        string? provider = null,
        string? from = null,
        string? to = null,
        string? cursor = null)
    {
        ModelFilter = model;
        ProviderFilter = provider;
        FromUtc = from;
        ToUtc = to;
        Cursor = cursor;

        DateTimeOffset? fromParsed = DateTimeOffset.TryParse(from, out var fp) ? fp : null;
        DateTimeOffset? toParsed = DateTimeOffset.TryParse(to, out var tp) ? tp : null;

        try
        {
            var (billingTask, nodesTask) = (
                billingClient.ListAdminRequestsAsync(
                    afterCursor: cursor,
                    pageSize: 50,
                    modelFilter: model,
                    providerFilter: provider,
                    fromUtc: fromParsed,
                    toUtc: toParsed),
                controlPlaneClient.ListRouterNodesAsync());

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
            ErrorMessage = "Failed to load requests.";
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
}
