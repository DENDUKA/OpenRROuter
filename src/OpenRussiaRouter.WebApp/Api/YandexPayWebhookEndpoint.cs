using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// Receives Yandex Pay webhook JWTs at <c>POST /v1/webhook</c>.
/// </summary>
/// <remarks>
/// The compact JWT body is passed unchanged to <see cref="IYandexPayWebhookVerifier"/> before the
/// endpoint examines notification fields. The verifier owns ES256/JWK and merchant validation.
/// </remarks>
public static class YandexPayWebhookEndpoint
{
    /// <summary>Yandex Pay webhook route.</summary>
    public const string Route = "/v1/webhook";

    /// <summary>Hard cap on a compact-JWT webhook body.</summary>
    public const int MaxBodyBytes = PaymentWebhookEndpoint.MaxBodyBytes;

    /// <summary>Maps the anonymous Yandex Pay webhook endpoint.</summary>
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Route, HandleAsync).AllowAnonymous();

    /// <summary>
    /// Verifies the raw JWT, maps terminal Yandex states to canonical payment events, and delegates
    /// the state transition to Billing through <see cref="PaymentWebhookProcessor"/>.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        IYandexPayWebhookVerifier verifier,
        PaymentWebhookProcessor processor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("YandexPayWebhookEndpoint");

        if (http.Request.ContentLength is long length && length > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string rawJwt;
        try
        {
            using var reader = new StreamReader(
                new LimitedStream(http.Request.Body, MaxBodyBytes),
                leaveOpen: true);
            rawJwt = await reader.ReadToEndAsync(ct);
        }
        catch (PayloadTooLargeException)
        {
            logger.LogWarning("Yandex Pay webhook rejected: streamed body exceeds {Max} bytes.", MaxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (string.IsNullOrWhiteSpace(rawJwt))
        {
            return Results.BadRequest(new { code = StatusCodes.Status400BadRequest });
        }

        YandexPayWebhookNotification? notification;
        try
        {
            // Do not parse or deserialize rawJwt here. JWT verification must precede every claim read.
            notification = await verifier.VerifyAsync(rawJwt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Yandex Pay webhook could not be verified.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (notification is null)
        {
            logger.LogWarning("Yandex Pay webhook rejected: JWT verification failed.");
            return Results.Unauthorized();
        }

        var webhookEvent = MapNotification(notification);
        if (webhookEvent is null)
        {
            return Results.Ok(new { code = StatusCodes.Status200OK });
        }

        var result = await processor.ProcessAsync(
            providerName: "yandexpay",
            webhookEvent,
            rawPayload: rawJwt,
            signatureOk: true,
            ct);

        return result.Outcome switch
        {
            PaymentWebhookProcessor.ProcessOutcome.Applied or PaymentWebhookProcessor.ProcessOutcome.Ignored =>
                Results.Ok(new { code = StatusCodes.Status200OK }),
            // Acknowledged on purpose: the verdict is final, and Yandex Pay reads any non-2xx as
            // "retry" — for 24h with backoff. The error still travels in the body and the log.
            PaymentWebhookProcessor.ProcessOutcome.RejectedPermanent => Results.Ok(new
            {
                code = StatusCodes.Status200OK,
                error = result.ErrorCode,
            }),
            PaymentWebhookProcessor.ProcessOutcome.Rejected => Results.UnprocessableEntity(new
            {
                code = StatusCodes.Status422UnprocessableEntity,
                error = result.ErrorCode,
            }),
            _ => Results.Ok(new { code = StatusCodes.Status200OK }),
        };
    }

    private static PaymentWebhookEvent? MapNotification(YandexPayWebhookNotification notification)
    {
        var eventType = notification.Status.Trim().ToUpperInvariant() switch
        {
            "CAPTURED" => PaymentWebhookEventTypes.Paid,
            "FAILED" => PaymentWebhookEventTypes.Canceled,
            "REFUNDED" => PaymentWebhookEventTypes.Refunded,
            _ => null,
        };

        return eventType is null
            ? null
            : new PaymentWebhookEvent(
                notification.ProviderPaymentId,
                notification.OrderId,
                eventType,
                notification.Amount,
                notification.Currency,
                notification.EventId,
                notification.Reason);
    }
}

/// <summary>
/// Boundary implemented by the Yandex Pay adapter. A non-null result means the JWT signature,
/// lifetime, algorithm, JWK key and merchant id have all been validated.
/// </summary>
public interface IYandexPayWebhookVerifier
{
    /// <summary>Verify a raw compact JWT and return its normalized notification, or null if invalid.</summary>
    Task<YandexPayWebhookNotification?> VerifyAsync(string rawJwt, CancellationToken ct);
}

/// <summary>Verified, normalized fields from a Yandex Pay notification.</summary>
public sealed record YandexPayWebhookNotification(
    string? ProviderPaymentId,
    string? OrderId,
    string Status,
    decimal? Amount,
    string? Currency,
    string? EventId,
    string? Reason);
