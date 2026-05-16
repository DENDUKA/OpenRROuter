using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodesModel : PageModelBase
{
    private readonly ControlPlaneClient _controlPlaneClient;

    public IReadOnlyList<RouterNodeInfo> Nodes { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];

    [BindProperty] public NodeFormInput Form { get; set; } = new();

    public NodesModel(ControlPlaneClient controlPlaneClient) => _controlPlaneClient = controlPlaneClient;

    public async Task OnGetAsync()
    {
        try { Nodes = await _controlPlaneClient.ListRouterNodesAsync(); }
        catch { ErrorMessage = "Failed to load router nodes."; }
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        try
        {
            await _controlPlaneClient.RegisterRouterNodeAsync(Form.Name, Form.EndpointUrl, Form.TokenSecretName, Form.CapacityWeight);
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to add router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableAsync(string nodeId)
    {
        try
        {
            await _controlPlaneClient.DisableRouterNodeAsync(nodeId);
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to disable router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public static string GetStatusBadge(string status) => status switch
    {
        "healthy" => "badge-success",
        "degraded" => "badge-warning",
        "unhealthy" => "badge-danger",
        _ => "badge-secondary"
    };

    public class NodeFormInput
    {
        public string Name { get; set; } = "";
        public string EndpointUrl { get; set; } = "";
        public string TokenSecretName { get; set; } = "";
        public int CapacityWeight { get; set; } = 100;
    }
}
