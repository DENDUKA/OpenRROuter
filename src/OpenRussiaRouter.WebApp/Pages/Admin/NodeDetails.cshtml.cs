using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeDetailsModel(
    ControlPlaneProvider ControlPlaneProvider,
    ILogger<NodeDetailsModel> logger) : PageModelBase
{
    public RouterNodeDetails? Details { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [BindProperty]
    public ControlTokenInput TokenForm { get; set; } = new();

    public async Task OnGetAsync(string nodeId)
    {
        try
        {
            Details = await ControlPlaneProvider.GetRouterNodeDetailsAsync(nodeId);
            TokenForm.ControlToken = Details.Node.ControlToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load router node details. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to load router node details.";
        }
    }

    public async Task<IActionResult> OnPostUpdateControlTokenAsync(string nodeId)
    {
        try
        {
            await ControlPlaneProvider.UpdateRouterNodeControlTokenAsync(
                nodeId,
                TokenForm.ControlToken,
                HttpContext.RequestAborted);
            SuccessMessage = "Control token updated.";
            return RedirectToPage(new { nodeId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update router node control token. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to update control token.";
            await OnGetAsync(nodeId);
            TokenForm.ControlToken = "";
            return Page();
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

    public sealed class ControlTokenInput
    {
        public string ControlToken { get; set; } = "";
    }
}
