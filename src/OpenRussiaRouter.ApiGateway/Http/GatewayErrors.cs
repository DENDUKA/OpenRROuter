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
            _ => StatusCodes.Status500InternalServerError
        };
        context.Response.ContentType = "application/json";

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
