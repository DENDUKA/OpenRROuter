using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("admin/gatewaytest")]
public sealed class GatewayTestController(
    AuthProvider authProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<GatewayTestController> logger) : Controller
{
    [HttpPost("stream")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StreamAsync([FromForm] StreamRequest form)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(form.Model))
            return await WriteErrorAsync(StatusCodes.Status400BadRequest, "Model is required.");

        if (string.IsNullOrWhiteSpace(form.Prompt))
            return await WriteErrorAsync(StatusCodes.Status400BadRequest, "Prompt is required.");

        var rawKey = await authProvider.GetFirstActiveApiKeyRawAsync(User.GetUserId(), HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(rawKey))
            return await WriteErrorAsync(
                StatusCodes.Status400BadRequest,
                "Нет активных API-ключей. Создайте ключ в разделе Рабочее пространство -> API Ключи.");

        var requestJson = JsonSerializer.Serialize(new
        {
            model = form.Model,
            stream = true,
            stream_options = new { include_usage = true },
            messages = new[] { new { role = "user", content = form.Prompt } }
        });

        var stopwatch = Stopwatch.StartNew();
        int upstreamStatus = 0;

        try
        {
            var client = httpClientFactory.CreateClient("ApiGateway");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
            request.Headers.TryAddWithoutValidation("X-RWT-Admin-Test", "1");
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted);

            upstreamStatus = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
                return await WriteErrorAsync(upstreamStatus, errorBody);
            }

            Response.StatusCode = upstreamStatus;
            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
            await Response.StartAsync(HttpContext.RequestAborted);

            await using var upstream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            var buffer = new byte[8192];
            while (true)
            {
                var read = await upstream.ReadAsync(buffer, HttpContext.RequestAborted);
                if (read == 0) break;
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gateway test streaming failed. Model={Model}", form.Model);
            if (!Response.HasStarted)
                return await WriteErrorAsync(StatusCodes.Status500InternalServerError, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
        }

        if (Response.HasStarted)
        {
            var metaPayload = JsonSerializer.Serialize(new
            {
                latency_ms = stopwatch.ElapsedMilliseconds,
                status = upstreamStatus
            });
            var meta = $"\nevent: rwt-meta\ndata: {metaPayload}\n\n";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(meta), HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }

        return new EmptyResult();
    }

    private async Task<IActionResult> WriteErrorAsync(int statusCode, string message)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.WriteAsync(message, HttpContext.RequestAborted);
        return new EmptyResult();
    }

    public sealed class StreamRequest
    {
        public string Model { get; set; } = "";
        public string Prompt { get; set; } = "";
    }
}
