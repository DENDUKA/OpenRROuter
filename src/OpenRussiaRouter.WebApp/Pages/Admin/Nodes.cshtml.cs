using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;
using System.Text;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodesModel(
    ControlPlaneProvider ControlPlaneProvider,
    AuthProvider AuthProvider,
    IHttpClientFactory HttpClientFactory,
    ILogger<NodesModel> logger) : PageModelBase
{
    public IReadOnlyList<RouterNodeInfo> Nodes { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];

    [BindProperty] public NodeFormInput Form { get; set; } = new();
    [BindProperty] public TestFormInput StreamingTestInput { get; set; } = new();

    public async Task OnGetAsync()
    {
        try { Nodes = await ControlPlaneProvider.ListRouterNodesAsync(); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load router nodes.");
            ErrorMessage = "Failed to load router nodes.";
        }
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        try
        {
            await ControlPlaneProvider.RegisterRouterNodeAsync(Form.Name, Form.EndpointUrl, Form.ControlToken, Form.CapacityWeight);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnPostAddAsync failed. Name={Name} Endpoint={Endpoint}", Form.Name, Form.EndpointUrl);
            ErrorMessage = $"Failed to add router node: {ex.GetType().Name}: {ex.Message}";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableAsync(string nodeId)
    {
        try
        {
            await ControlPlaneProvider.DisableRouterNodeAsync(nodeId);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnPostDisableAsync failed. NodeId={NodeId}", nodeId);
            ErrorMessage = "Failed to disable router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string nodeId)
    {
        try
        {
            await ControlPlaneProvider.DeleteRouterNodeAsync(nodeId);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnPostDeleteAsync failed. NodeId={NodeId}", nodeId);
            ErrorMessage = "Failed to delete router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEnableAsync(string nodeId)
    {
        try
        {
            await ControlPlaneProvider.EnableRouterNodeAsync(nodeId);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnPostEnableAsync failed. NodeId={NodeId}", nodeId);
            ErrorMessage = "Failed to enable router node.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnGetNodeModelsAsync(string nodeId)
    {
        var models = await ControlPlaneProvider.GetNodeModelsAsync(nodeId, HttpContext.RequestAborted);
        return new JsonResult(models);
    }

    public async Task<IActionResult> OnPostStreamingTestAsync()
    {
        await OnGetAsync();

        Response.ContentType = "text/plain; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";

        var node = FindSelectedNode(StreamingTestInput.NodeId);
        if (node is null)
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest, "Selected router node was not found.");

        if (string.IsNullOrWhiteSpace(StreamingTestInput.Model))
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest, "Model is required.");

        if (string.IsNullOrWhiteSpace(node.ControlToken))
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest, "Selected router node has no Control Token saved.");

        var rawKey = await AuthProvider.GetFirstActiveApiKeyRawAsync(User.GetUserId(), HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(rawKey))
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest,
                "Нет активных API-ключей. Создайте ключ в разделе Рабочее пространство → API Ключи.");

        try
        {
            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = StreamingTestInput.Model,
                stream = true,
                messages = new[] { new { role = "user", content = StreamingTestInput.Message } }
            });

            var client = HttpClientFactory.CreateClient("DirectNodeChat");
            using var request = CreateDirectNodeRequest(node, rawKey, requestBody);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
                var friendlyMessage = TryExtractErrorMessage(errorBody) ?? errorBody;
                return await WriteStreamingErrorAsync((int)response.StatusCode, friendlyMessage);
            }

            Response.StatusCode = (int)response.StatusCode;
            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain; charset=utf-8";
            await Response.StartAsync(HttpContext.RequestAborted);

            await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, HttpContext.RequestAborted);
                if (read == 0) break;
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Node streaming test request failed. NodeId={NodeId}; Model={Model}",
                StreamingTestInput.NodeId,
                StreamingTestInput.Model);
            if (!Response.HasStarted)
                return await WriteStreamingErrorAsync(StatusCodes.Status500InternalServerError, ex.Message);
            await Response.WriteAsync($"{Environment.NewLine}[stream error] {ex.Message}", HttpContext.RequestAborted);
        }

        return new EmptyResult();
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var messageProp))
            {
                var message = messageProp.GetString();
                if (message is null) return null;
                return TryFlattenEmbeddedJson(message) ?? message;
            }
        }
        catch { }
        return null;
    }

    private RouterNodeInfo? FindSelectedNode(string nodeId) =>
        Nodes.FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));

    private static HttpRequestMessage CreateDirectNodeRequest(
        RouterNodeInfo node,
        string rawKey,
        string requestBody)
    {
        if (string.IsNullOrWhiteSpace(node.EndpointUrl))
            throw new InvalidOperationException("Router node endpoint URL is empty.");

        var baseUri = new Uri(node.EndpointUrl, UriKind.Absolute);
        var requestUri = new Uri(baseUri, "/v1/chat/completions");
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + rawKey);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return request;
    }

    // Handles "[prefix] [NNN]: {json_blob} (suffix)" format from upstream providers
    private static string? TryFlattenEmbeddedJson(string message)
    {
        var braceIdx = message.IndexOf('{');
        if (braceIdx < 0) return null;

        var lastBrace = message.LastIndexOf('}');
        if (lastBrace <= braceIdx) return null;

        try
        {
            var prefix = message[..braceIdx].TrimEnd(':', ' ');
            var suffix = message[(lastBrace + 1)..].Trim();
            var jsonPart = message[braceIdx..(lastBrace + 1)];

            using var inner = System.Text.Json.JsonDocument.Parse(jsonPart);
            if (inner.RootElement.TryGetProperty("error", out var innerError) &&
                innerError.TryGetProperty("message", out var innerMsg))
            {
                var innerText = innerMsg.GetString();
                if (!string.IsNullOrWhiteSpace(innerText))
                {
                    var parts = new[] { prefix, innerText, suffix }
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    return string.Join(" ", parts);
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<IActionResult> WriteStreamingErrorAsync(int statusCode, string message)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.WriteAsync(message, HttpContext.RequestAborted);
        return new EmptyResult();
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
        public string ControlToken { get; set; } = "";
        public int CapacityWeight { get; set; } = 100;
    }

    public class TestFormInput
    {
        public string NodeId { get; set; } = "";
        public string Model { get; set; } = "minimax/MiniMax-M2.7";
        public string Message { get; set; } = "";
    }
}
