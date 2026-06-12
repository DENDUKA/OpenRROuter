using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodeEditModel(
    ControlPlaneProvider ControlPlaneProvider,
    RouterNodeApiProvider RouterNodeApiProvider,
    ILogger<NodeEditModel> logger) : PageModelBase
{
    public RouterNodeInfo? Node { get; private set; }

    public string? ErrorMessage { get; private set; }

    public RouterNodeAvailabilityCheckResult? AvailabilityCheckResult { get; private set; }

    public RouterNodeProbeResult? CatalogProbeResult { get; private set; }

    [BindProperty]
    public NodeEditInput Form { get; set; } = new();

    public async Task OnGetAsync(string nodeId)
    {
        try
        {
            await LoadNodeAsync(nodeId, populateForm: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load router node edit page. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to load router node.";
        }
    }

    public async Task<IActionResult> OnPostCheckAsync(string nodeId)
    {
        try
        {
            await LoadNodeAsync(nodeId, populateForm: false);
            if (Node is null)
            {
                ErrorMessage = "Router node was not found.";
                return Page();
            }

            AvailabilityCheckResult = await RouterNodeApiProvider.CheckIntegrationApiAvailabilityAsync(
                Node with { EndpointUrl = Form.EndpointUrl, ControlToken = Form.ControlToken },
                Form.ControlToken,
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check router node edit form. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to check node availability.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostProbeCatalogAsync(string nodeId)
    {
        try
        {
            await LoadNodeAsync(nodeId, populateForm: false);
            if (Node is null)
            {
                ErrorMessage = "Router node was not found.";
                return Page();
            }

            CatalogProbeResult = await RouterNodeApiProvider.ProbeProvidersAsync(
                Node with { EndpointUrl = Form.EndpointUrl, ControlToken = Form.ControlToken },
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to probe router node catalog endpoint. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to probe catalog endpoint.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string nodeId)
    {
        try
        {
            await ControlPlaneProvider.UpdateRouterNodeAsync(
                nodeId,
                Form.Name,
                Form.EndpointUrl,
                Form.ControlToken,
                Form.CapacityWeight,
                HttpContext.RequestAborted);
            return RedirectToPage("/Admin/NodeDetails", new { nodeId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update router node. NodeId: {NodeId}", nodeId);
            ErrorMessage = "Failed to update router node.";
            await LoadNodeAsync(nodeId, populateForm: false);
            return Page();
        }
    }

    private async Task LoadNodeAsync(string nodeId, bool populateForm)
    {
        try
        {
            var details = await ControlPlaneProvider.GetRouterNodeDetailsAsync(nodeId, cancellationToken: HttpContext.RequestAborted);
            Node = details.Node;
        }
        catch
        {
            Node = null;
        }

        if (Node is null)
        {
            ErrorMessage = "Router node was not found.";
            return;
        }

        if (populateForm)
        {
            Form = new NodeEditInput
            {
                Name = Node.Name,
                EndpointUrl = Node.EndpointUrl,
                ControlToken = Node.ControlToken,
                CapacityWeight = Node.CapacityWeight > 0 ? Node.CapacityWeight : 100
            };
        }
    }

    public sealed class NodeEditInput
    {
        public string Name { get; set; } = "";

        public string EndpointUrl { get; set; } = "";

        public string ControlToken { get; set; } = "";

        public int CapacityWeight { get; set; } = 100;
    }
}
