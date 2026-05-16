using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodesModel : PageModelBase
{
    private readonly ControlPlaneClient _controlPlaneClient;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public IReadOnlyList<RouterNodeInfo> Nodes { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];

    [BindProperty] public NodeFormInput Form { get; set; } = new();
    [BindProperty] public TestFormInput TestInput { get; set; } = new();
    public TestFormResult? TestResult { get; set; }

    [BindProperty] public string? FetchModelsNodeId { get; set; }
    public IReadOnlyList<string> NodeModels { get; set; } = [];
    public string? NodeModelsName { get; set; }

    public NodesModel(
        ControlPlaneClient controlPlaneClient,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _controlPlaneClient = controlPlaneClient;
        _httpClient = httpClientFactory.CreateClient("ApiGateway");
        _configuration = configuration;
    }

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

    public async Task<IActionResult> OnPostDeleteAsync(string nodeId)
    {
        try
        {
            await _controlPlaneClient.DeleteRouterNodeAsync(nodeId);
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to delete router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEnableAsync(string nodeId)
    {
        try
        {
            await _controlPlaneClient.EnableRouterNodeAsync(nodeId);
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to enable router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostFetchModelsAsync()
    {
        await OnGetAsync();
        if (string.IsNullOrWhiteSpace(FetchModelsNodeId))
            return Page();

        var node = Nodes.FirstOrDefault(n => n.NodeId == FetchModelsNodeId);
        if (node is null)
        {
            ErrorMessage = "Node not found.";
            return Page();
        }

        try
        {
            var modelsUrl = $"{node.EndpointUrl.TrimEnd('/')}/v1/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to fetch models from node: HTTP {(int)resp.StatusCode}";
                return Page();
            }

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataEl))
            {
                NodeModels = dataEl.EnumerateArray()
                    .Where(m => m.TryGetProperty("id", out _))
                    .Select(m => m.GetProperty("id").GetString() ?? "")
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .OrderBy(id => id)
                    .ToList();
            }

            NodeModelsName = node.Name;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to fetch models: {ex.Message}";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var gatewayUrl = _configuration["ApiGateway:Url"]?.TrimEnd('/') ?? "http://localhost";
        try
        {
            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = TestInput.Model,
                messages = new[] { new { role = "user", content = TestInput.Message } }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{gatewayUrl}/v1/chat/completions");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {TestInput.ApiKey}");
            req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            TestResult = new TestFormResult(resp.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            TestResult = new TestFormResult(false, ex.Message);
        }

        await OnGetAsync();
        return Page();
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

    public class TestFormInput
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "minimax/MiniMax-M2.7";
        public string Message { get; set; } = "";
    }

    public sealed record TestFormResult(bool Success, string Body);
}
