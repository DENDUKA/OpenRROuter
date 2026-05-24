using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeProviderLimitsModel(
    ControlPlaneProvider ControlPlaneProvider,
    ILogger<NodeProviderLimitsModel> logger) : PageModelBase
{
    public string NodeId { get; private set; } = string.Empty;
    public string NodeName { get; private set; } = string.Empty;
    public IReadOnlyList<ProviderLimitView> Providers { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string nodeId)
    {
        NodeId = nodeId;
        try
        {
            var details = await ControlPlaneProvider.GetRouterNodeDetailsAsync(nodeId);
            NodeName = details.Node.Name;
            Providers = details.Providers.Select(ParseProvider).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load provider limits. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to load provider limits.";
        }
    }

    public static string GetProgressBarClass(double remainingPercentage) => remainingPercentage switch
    {
        > 50 => "bg-success",
        > 20 => "bg-warning",
        _ => "bg-danger"
    };

    public static string GetStatusBadge(bool isActive) =>
        isActive ? "badge-success" : "badge-secondary";

    private ProviderLimitView ParseProvider(RouterProviderQuotaInfo p)
    {
        var quotas = ParseQuotas(p.UsageJson, p.ConnectionId);
        return new ProviderLimitView(p.ConnectionId, p.Provider, p.Name, p.IsActive,
            p.QuotaSupported, p.Error, quotas);
    }

    private IReadOnlyList<QuotaView> ParseQuotas(string? usageJson, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(usageJson))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(usageJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("quotas", out var quotasEl) ||
                quotasEl.ValueKind != JsonValueKind.Object)
                return [];

            var result = new List<QuotaView>();
            foreach (var prop in quotasEl.EnumerateObject())
            {
                var entry = prop.Value;

                var remaining = entry.TryGetProperty("remainingPercentage", out var pctEl)
                    ? pctEl.GetDouble()
                    : 0;

                string? resetAt = null;
                if (entry.TryGetProperty("resetAt", out var resetEl) &&
                    resetEl.ValueKind == JsonValueKind.String)
                    resetAt = resetEl.GetString();

                var unlimited = entry.TryGetProperty("unlimited", out var unlEl) &&
                                unlEl.ValueKind == JsonValueKind.True;

                result.Add(new QuotaView(prop.Name, remaining, resetAt, unlimited));
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse quota JSON from router node. ConnectionId={ConnectionId}", connectionId);
            return [];
        }
    }
}

public sealed record ProviderLimitView(
    string ConnectionId,
    string Provider,
    string Name,
    bool IsActive,
    bool QuotaSupported,
    string? Error,
    IReadOnlyList<QuotaView> Quotas);

public sealed record QuotaView(
    string Label,
    double RemainingPercentage,
    string? ResetAt,
    bool Unlimited);
