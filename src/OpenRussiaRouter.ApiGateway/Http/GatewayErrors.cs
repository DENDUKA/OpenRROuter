using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.Http;

/// <summary>
/// Creates public OpenAI-compatible-ish Gateway error responses.
/// </summary>
public static class GatewayErrors
{
    /// <summary>
    /// Creates an authentication error response.
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Safe message.</param>
    /// <returns>HTTP result.</returns>
    public static IResult Authentication(HttpRequest request, string code, string message)
    {
        return Results.Json(
            new
            {
                error = new ApiError(
                    "authentication_error",
                    code,
                    message,
                    CorrelationIdReader.Get(request))
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Creates a service unavailable response.
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Safe message.</param>
    /// <returns>HTTP result.</returns>
    public static IResult ServiceUnavailable(HttpRequest request, string code, string message)
    {
        return Results.Json(
            new
            {
                error = new ApiError(
                    "service_unavailable",
                    code,
                    message,
                    CorrelationIdReader.Get(request))
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Creates a validation error response.
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Safe message.</param>
    /// <returns>HTTP result.</returns>
    public static IResult Validation(HttpRequest request, string code, string message)
    {
        return Results.Json(
            new
            {
                error = new ApiError(
                    "invalid_request_error",
                    code,
                    message,
                    CorrelationIdReader.Get(request))
            },
            statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Creates a billing error response.
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Safe message.</param>
    /// <returns>HTTP result.</returns>
    public static IResult Billing(HttpRequest request, string code, string message)
    {
        return Results.Json(
            new
            {
                error = new ApiError(
                    "billing_error",
                    code,
                    message,
                    CorrelationIdReader.Get(request))
            },
            statusCode: StatusCodes.Status402PaymentRequired);
    }
}
