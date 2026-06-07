using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeDetailsModel(
    ControlPlaneProvider ControlPlaneProvider,
    RouterNodeApiProvider RouterNodeApiProvider,
    ILogger<NodeDetailsModel> logger) : PageModelBase
{
    public RouterNodeDetails? Details { get; private set; }

    public string? ErrorMessage { get; private set; }

    public RouterNodeAvailabilityCheckResult? AvailabilityCheckResult { get; private set; }

    public RouterNodeProbeResult? CatalogProbeResult { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [BindProperty]
    public ControlTokenInput TokenForm { get; set; } = new();

    public async Task OnGetAsync(string nodeId)
    {
        try
        {
            await LoadDetailsAsync(nodeId, populateTokenForm: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load router node details. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to load router node details.";
        }
    }

    public async Task<IActionResult> OnPostCheckControlTokenAsync(string nodeId)
    {
        try
        {
            await LoadDetailsAsync(nodeId, populateTokenForm: false);
            if (Details is null)
            {
                ErrorMessage = "Failed to load router node details.";
                return Page();
            }

            AvailabilityCheckResult = await RouterNodeApiProvider.CheckIntegrationApiAvailabilityAsync(
                Details.Node,
                TokenForm.ControlToken,
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check router node control token. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to check control token.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostProbeCatalogAsync(string nodeId)
    {
        try
        {
            await LoadDetailsAsync(nodeId, populateTokenForm: false);
            if (Details is null)
            {
                ErrorMessage = "Failed to load router node details.";
                return Page();
            }

            CatalogProbeResult = await RouterNodeApiProvider.ProbeProvidersAsync(
                Details.Node with { ControlToken = TokenForm.ControlToken },
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to probe router node catalog endpoint. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to probe catalog endpoint.";
        }

        return Page();
    }

    private async Task LoadDetailsAsync(string nodeId, bool populateTokenForm)
    {
        Details = await ControlPlaneProvider.GetRouterNodeDetailsAsync(nodeId);
        if (populateTokenForm)
        {
            TokenForm.ControlToken = Details.Node.ControlToken;
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

    public async Task<IActionResult> OnPostSyncKeysAsync(string nodeId)
    {
        try
        {
            var result = await ControlPlaneProvider.SyncNodeIntegrationKeysAsync(nodeId, HttpContext.RequestAborted);
            SuccessMessage = $"Sync complete. Added: {result.Added}, Disabled: {result.Disabled}, Reactivated: {result.Reactivated}, Deleted: {result.Deleted}.";
            return RedirectToPage(new { nodeId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync keys for node. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to sync integration keys.";
            await OnGetAsync(nodeId);
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
