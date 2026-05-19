using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeDetailsModel(
    ControlPlaneClient controlPlaneClient,
    ILogger<NodeDetailsModel> logger) : PageModelBase
{
    public RouterNodeDetails? Details { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string nodeId)
    {
        try
        {
            Details = await controlPlaneClient.GetRouterNodeDetailsAsync(nodeId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load router node details. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to load router node details.";
        }
    }

    public static string GetStatusBadge(bool isActive) =>
        isActive ? "badge-success" : "badge-secondary";

    public static string GetNodeStatusBadge(string status) => status switch
    {
        "healthy" => "badge-success",
        "degraded" => "badge-warning",
        "unhealthy" => "badge-danger",
        "disabled" => "badge-secondary",
        _ => "badge-secondary"
    };

    public static string FormatJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }
}
