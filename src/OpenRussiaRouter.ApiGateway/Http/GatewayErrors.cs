using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Http;

public static class GatewayErrors
{
    public static IResult Authentication(HttpRequest request, string code, string message)
        => Error(request, StatusCodes.Status401Unauthorized, "authentication_error", code, message);

    public static IResult ServiceUnavailable(HttpRequest request, string code, string message)
        => Error(request, StatusCodes.Status503ServiceUnavailable, "service_unavailable", code, message);

    public static IResult Validation(HttpRequest request, string code, string message)
        => Error(request, StatusCodes.Status400BadRequest, "invalid_request_error", code, message);

    public static IResult Billing(HttpRequest request, string code, string message)
        => Error(request, StatusCodes.Status402PaymentRequired, "billing_error", code, message);

    public static IResult Internal(HttpRequest request)
        => Error(request, StatusCodes.Status500InternalServerError, "internal_error", ErrorCodes.InternalError, "Internal server error.");

    /// <summary>
    /// Записывает JSON-ошибку в формате Anthropic: {"type":"error","error":{"type":...,"message":...}}.
    /// </summary>
    public static async Task WriteAnthropicAsync(HttpContext context, string type, string code, string message, string requestId)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = type switch
        {
            "authentication_error" => StatusCodes.Status401Unauthorized,
            "billing_error" => StatusCodes.Status402PaymentRequired,
            "permission_error" => StatusCodes.Status403Forbidden,
            "not_found_error" => StatusCodes.Status404NotFound,
            "invalid_request_error" => StatusCodes.Status400BadRequest,
            "service_unavailable" or "api_error" => StatusCodes.Status503ServiceUnavailable,
            "rate_limit_error" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };

        var anthropicType = type switch
        {
            "authentication_error" => "authentication_error",
            "billing_error" => "invalid_request_error", // Anthropic has no billing_error, maps to invalid_request_error or 402
            "invalid_request_error" => "invalid_request_error",
            "rate_limit_error" => "rate_limit_error",
            "service_unavailable" => "api_error",
            "not_found_error" => "not_found_error",
            "permission_error" => "permission_error",
            _ => "api_error"
        };

        context.Response.ContentType = "application/json";
        context.Response.Headers[CorrelationIds.HeaderName] = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;

        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            type = "error",
            error = new { type = anthropicType, message = message }
        });
    }

    /// <summary>
    /// Записывает JSON-ошибку напрямую в <see cref="HttpContext.Response"/>.
    /// Используется в async Task обработчиках (SSE, streaming), где возврат IResult невозможен.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, string type, string code, string message, string requestId)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = type switch
        {
            "authentication_error" => StatusCodes.Status401Unauthorized,
            "billing_error" => StatusCodes.Status402PaymentRequired,
            "invalid_request_error" => StatusCodes.Status400BadRequest,
            "service_unavailable" => StatusCodes.Status503ServiceUnavailable,
            "rate_limit_error" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };
        context.Response.ContentType = "application/json";
        context.Response.Headers[CorrelationIds.HeaderName] = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;

        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            error = new { type, code, message, request_id = requestId }
        });
    }

    private static IResult Error(HttpRequest request, int statusCode, string type, string code, string message)
    {
        var requestId = CorrelationIdReader.Get(request);
        request.HttpContext.Response.Headers[CorrelationIds.HeaderName] = requestId;
        request.HttpContext.Response.Headers["X-Request-Id"] = requestId;

        return Results.Json(
            new { error = new { type, code, message, request_id = requestId } },
            statusCode: statusCode);
    }
}
