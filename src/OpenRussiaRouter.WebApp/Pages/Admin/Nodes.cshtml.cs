using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class NodesModel(
    ControlPlaneClient controlPlaneClient,
    AuthClient authClient,
    BillingClient billingClient,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<NodesModel> logger) : PageModelBase
{
    private const string ModelPriceMissingErrorCode = "model_price_missing";

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("RouterNode");

    public IReadOnlyList<RouterNodeInfo> Nodes { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];

    [BindProperty] public NodeFormInput Form { get; set; } = new();
    [BindProperty] public TestFormInput TestInput { get; set; } = new();
    [BindProperty] public TestFormInput StreamingTestInput { get; set; } = new();
    public TestFormResult? TestResult { get; set; }

    public async Task OnGetAsync()
    {
        try { Nodes = await controlPlaneClient.ListRouterNodesAsync(); }
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
            await controlPlaneClient.RegisterRouterNodeAsync(Form.Name, Form.EndpointUrl, Form.ControlToken, Form.CapacityWeight);
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
            await controlPlaneClient.DisableRouterNodeAsync(nodeId);
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
            await controlPlaneClient.DeleteRouterNodeAsync(nodeId);
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
            await controlPlaneClient.EnableRouterNodeAsync(nodeId);
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
        try { Nodes = await controlPlaneClient.ListRouterNodesAsync(); }
        catch { return new JsonResult(Array.Empty<string>()); }

        var node = Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node is null)
            return new JsonResult(Array.Empty<string>());

        try
        {
            var modelsUrl = $"{node.EndpointUrl.TrimEnd('/')}/v1/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            AddNodeTokenHeaders(req, node);
            using var resp = await _httpClient.SendAsync(req);

            if (!resp.IsSuccessStatusCode)
                return new JsonResult(Array.Empty<string>());

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataEl))
            {
                var models = dataEl.EnumerateArray()
                    .Where(m => m.TryGetProperty("id", out _))
                    .Select(m => m.GetProperty("id").GetString() ?? "")
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .OrderBy(id => id)
                    .ToList();
                return new JsonResult(models);
            }

            return new JsonResult(Array.Empty<string>());
        }
        catch
        {
            return new JsonResult(Array.Empty<string>());
        }
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        await OnGetAsync();

        var node = string.IsNullOrWhiteSpace(TestInput.NodeId)
            ? Nodes.FirstOrDefault(n => n.Status != "disabled")
            : Nodes.FirstOrDefault(n => n.NodeId == TestInput.NodeId);

        if (node is null)
        {
            TestResult = new TestFormResult(false, "Выберите ноду перед отправкой тестового запроса.");
            return Page();
        }

        TestInput.NodeId = node.NodeId;

        var userId = User.GetUserId();
        var userKeys = await authClient.ListApiKeysAsync(userId, HttpContext.RequestAborted);
        var activeKey = userKeys.FirstOrDefault(k => k.IsActive);
        if (activeKey is null)
        {
            TestResult = new TestFormResult(false,
                "Нет личных API-ключей. Создайте ключ в разделе Рабочее пространство → API Ключи.");
            return Page();
        }

        var rawKey = await authClient.GetFirstActiveApiKeyRawAsync(userId, HttpContext.RequestAborted);

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            TestResult = new TestFormResult(false,
                "Нет активных API-ключей с сохранённым значением. Пересоздайте ключ в разделе Рабочее пространство → API Ключи.");
            return Page();
        }

        try
        {
            await controlPlaneClient.PushUserIntegrationKeyAsync(
                $"user:{userId}", rawKey, userId, apiKeyId: string.Empty, nodeId: node.NodeId,
                cancellationToken: HttpContext.RequestAborted);

            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = TestInput.Model,
                messages = new[] { new { role = "user", content = TestInput.Message } }
            });

            var startedAtUtc = DateTimeOffset.UtcNow;
            var chatUrl = $"{node.EndpointUrl.TrimEnd('/')}/v1/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, chatUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {rawKey}");
            req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, HttpContext.RequestAborted);
            var body = await resp.Content.ReadAsStringAsync();
            var completedAtUtc = DateTimeOffset.UtcNow;

            var warning = await RecordTestActivityAsync(
                userId,
                activeKey,
                TestInput.Model,
                node.NodeId,
                body,
                resp.IsSuccessStatusCode,
                startedAtUtc,
                completedAtUtc);

            TestResult = new TestFormResult(
                resp.IsSuccessStatusCode && warning is null,
                AppendWarning(body, warning));
        }
        catch (Exception ex)
        {
            TestResult = new TestFormResult(false, ex.Message);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostStreamingTestAsync()
    {
        await OnGetAsync();

        Response.ContentType = "text/plain; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";

        var node = string.IsNullOrWhiteSpace(StreamingTestInput.NodeId)
            ? Nodes.FirstOrDefault(n => n.Status != "disabled")
            : Nodes.FirstOrDefault(n => n.NodeId == StreamingTestInput.NodeId);

        if (node is null)
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest, "Select a node before sending a streaming test request.");

        StreamingTestInput.NodeId = node.NodeId;

        var userId = User.GetUserId();
        var userKeys = await authClient.ListApiKeysAsync(userId, HttpContext.RequestAborted);
        var activeKey = userKeys.FirstOrDefault(k => k.IsActive);
        if (activeKey is null)
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest, "No personal API keys. Create an API key in Workspace -> API Keys.");

        var rawKey = await authClient.GetFirstActiveApiKeyRawAsync(userId, HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(rawKey))
            return await WriteStreamingErrorAsync(StatusCodes.Status400BadRequest, "No active API key with a stored raw value. Recreate the key in Workspace -> API Keys.");

        try
        {
            await controlPlaneClient.PushUserIntegrationKeyAsync(
                $"user:{userId}", rawKey, userId, apiKeyId: string.Empty, nodeId: node.NodeId,
                cancellationToken: HttpContext.RequestAborted);

            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = StreamingTestInput.Model,
                stream = true,
                messages = new[] { new { role = "user", content = StreamingTestInput.Message } }
            });

            var startedAtUtc = DateTimeOffset.UtcNow;
            var chatUrl = $"{node.EndpointUrl.TrimEnd('/')}/v1/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, chatUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {rawKey}");
            req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(
                req,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted);

            Response.StatusCode = (int)resp.StatusCode;
            Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "text/plain; charset=utf-8";
            await Response.StartAsync(HttpContext.RequestAborted);

            await using var stream = await resp.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            var buffer = new byte[8192];
            var capturedBody = new System.Text.StringBuilder();
            while (true)
            {
                var read = await stream.ReadAsync(buffer, HttpContext.RequestAborted);
                if (read == 0)
                    break;

                capturedBody.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }

            var warning = await RecordTestActivityAsync(
                userId,
                activeKey,
                StreamingTestInput.Model,
                node.NodeId,
                capturedBody.ToString(),
                resp.IsSuccessStatusCode,
                startedAtUtc,
                DateTimeOffset.UtcNow);

            if (warning is not null)
            {
                await Response.WriteAsync($"{Environment.NewLine}[activity warning] {warning}", HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!Response.HasStarted)
                return await WriteStreamingErrorAsync(StatusCodes.Status500InternalServerError, ex.Message);

            await Response.WriteAsync($"{Environment.NewLine}[stream error] {ex.Message}", HttpContext.RequestAborted);
        }

        return new EmptyResult();
    }

    private async Task<string?> RecordTestActivityAsync(
        string userId,
        ApiKeyInfo apiKey,
        string modelId,
        string routerNodeId,
        string responseBody,
        bool isSuccessStatusCode,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        var hasUsage = TokenUsageExtractor.TryExtractJson(responseBody, out var usage) ||
                       TokenUsageExtractor.TryExtractStreamingSse(responseBody, out usage);
        var status = isSuccessStatusCode
            ? hasUsage ? "completed" : "usage_missing"
            : "failed";

        try
        {
            var errorCode = await billingClient.RecordUsageAsync(
                Guid.NewGuid().ToString("N"),
                userId,
                apiKey.Id,
                apiKey.Prefix,
                modelId,
                routerNodeId,
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheTokens,
                status,
                startedAtUtc,
                completedAtUtc,
                HttpContext.RequestAborted);

            return string.IsNullOrWhiteSpace(errorCode) || errorCode == ModelPriceMissingErrorCode
                ? null
                : $"BillingService returned {errorCode}.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record admin node test activity. UserId={UserId} ApiKeyId={ApiKeyId} NodeId={NodeId} Model={Model}",
                userId, apiKey.Id, routerNodeId, modelId);
            return ex.Message;
        }
    }

    private static string AppendWarning(string body, string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return body;
        }

        return $"[activity warning] {warning}{Environment.NewLine}{Environment.NewLine}{body}";
    }

    private async Task<IActionResult> WriteStreamingErrorAsync(int statusCode, string message)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.WriteAsync(message, HttpContext.RequestAborted);
        return new EmptyResult();
    }

    private void AddNodeTokenHeaders(HttpRequestMessage request, RouterNodeInfo node)
    {
        var nodeToken = ResolveNodeToken(node.ControlToken);
        if (string.IsNullOrWhiteSpace(nodeToken))
            return;

        request.Headers.TryAddWithoutValidation("X-ORR-Node-Token", nodeToken);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {nodeToken}");
    }

    private string ResolveNodeToken(string controlToken)
    {
        if (string.IsNullOrWhiteSpace(controlToken))
            return string.Empty;

        return configuration[$"RouterNodeTokens:{controlToken.Trim()}"] ?? string.Empty;
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

    public sealed record TestFormResult(bool Success, string Body);
}
