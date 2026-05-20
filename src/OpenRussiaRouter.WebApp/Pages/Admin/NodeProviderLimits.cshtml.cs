using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeProviderLimitsModel(
    ControlPlaneClient controlPlaneClient,
    ILogger<NodeProviderLimitsModel> logger) : PageModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string NodeId { get; private set; } = string.Empty;
    public string NodeName { get; private set; } = string.Empty;
    public IReadOnlyList<ProviderLimitView> Providers { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string nodeId)
    {
        NodeId = nodeId;
        try
        {
            var details = await controlPlaneClient.GetRouterNodeDetailsAsync(nodeId);
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

    private static ProviderLimitView ParseProvider(RouterProviderQuotaInfo p)
    {
        var quotas = ParseQuotas(p.UsageJson);
        return new ProviderLimitView(p.ConnectionId, p.Provider, p.Name, p.IsActive,
            p.QuotaSupported, p.Error, quotas);
    }

    private static IReadOnlyList<QuotaView> ParseQuotas(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson))
            return [];
        try
        {
            var usage = JsonSerializer.Deserialize<ProviderUsageDto>(usageJson, JsonOptions);
            if (usage?.Quotas is not { Count: > 0 })
                return [];

            return usage.Quotas
                .Select(kvp => new QuotaView(
                    kvp.Key,
                    kvp.Value.RemainingPercentage,
                    kvp.Value.ResetAt,
                    kvp.Value.Unlimited))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ProviderUsageDto
    {
        [JsonPropertyName("quotas")]
        public Dictionary<string, QuotaEntryDto>? Quotas { get; init; }
    }

    private sealed record QuotaEntryDto
    {
        [JsonPropertyName("remainingPercentage")]
        public double RemainingPercentage { get; init; }

        [JsonPropertyName("resetAt")]
        public string? ResetAt { get; init; }

        [JsonPropertyName("unlimited")]
        public bool Unlimited { get; init; }
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
