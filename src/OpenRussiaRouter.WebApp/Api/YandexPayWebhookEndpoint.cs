using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BuildingBlocks.Errors;
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

    // Yandex Pay documents a closed set of reasonCode values for non-200 answers; anything else
    // reads as an unknown failure on their side. Only the ones this endpoint can produce are here.
    private const string ReasonUnauthorized = "UNAUTHORIZED";
    private const string ReasonOrderNotFound = "ORDER_NOT_FOUND";
    private const string ReasonOrderAmountMismatch = "ORDER_AMOUNT_MISMATCH";
    private const string ReasonConflict = "CONFLICT";

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
            // Signature verification never ran, so from Yandex's side this is indistinguishable
            // from a token we could not authenticate.
            return Reject(StatusCodes.Status413PayloadTooLarge, ReasonUnauthorized);
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
            return Reject(StatusCodes.Status413PayloadTooLarge, ReasonUnauthorized);
        }

        if (string.IsNullOrWhiteSpace(rawJwt))
        {
            return Reject(StatusCodes.Status400BadRequest, ReasonUnauthorized);
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
            return Reject(StatusCodes.Status503ServiceUnavailable, ReasonConflict);
        }

        if (notification is null)
        {
            logger.LogWarning("Yandex Pay webhook rejected: JWT verification failed.");
            return Reject(StatusCodes.Status401Unauthorized, ReasonUnauthorized);
        }

        var webhookEvent = MapNotification(notification);
        if (webhookEvent is null)
        {
            return Ack();
        }

        var result = await processor.ProcessAsync(
            providerName: "yandexpay",
            webhookEvent,
            rawPayload: rawJwt,
            signatureOk: true,
            ct);

        switch (result.Outcome)
        {
            case PaymentWebhookProcessor.ProcessOutcome.RejectedPermanent:
                // Acknowledged on purpose: the verdict is final, and Yandex Pay reads any non-2xx
                // as "retry" — for 24h with backoff. The reason travels in the log, because the
                // ack body has no field for it.
                logger.LogError(
                    "Yandex Pay webhook acknowledged despite a final rejection: order={Order} status={Status} errorCode={ErrorCode}.",
                    notification.OrderId, notification.Status, result.ErrorCode);
                return Ack();

            case PaymentWebhookProcessor.ProcessOutcome.Rejected:
                return Reject(StatusCodes.Status422UnprocessableEntity, ToReasonCode(result.ErrorCode));

            default:
                return Ack();
        }
    }

    /// <summary>Body Yandex Pay expects on an accepted notification.</summary>
    private static IResult Ack() => Results.Ok(new { status = "success" });

    /// <summary>Non-200 answer carrying a documented <c>reasonCode</c>.</summary>
    private static IResult Reject(int statusCode, string reasonCode) =>
        Results.Json(new { reasonCode }, statusCode: statusCode);

    /// <summary>Translates a Billing rejection into the closest documented Yandex Pay reason.</summary>
    private static string ToReasonCode(string? errorCode) => errorCode switch
    {
        ErrorCodes.PaymentNotFound => ReasonOrderNotFound,
        ErrorCodes.PaymentAmountMismatch => ReasonOrderAmountMismatch,
        // Raised by PaymentWebhookProcessor when the confirmed amount could not be resolved at all.
        "AMOUNT_MISSING" => ReasonOrderAmountMismatch,
        _ => ReasonConflict,
    };

    private static PaymentWebhookEvent? MapNotification(YandexPayWebhookNotification notification)
    {
        // Keep in step with YandexPayPaymentProvider.MapStatus — the reconciliation worker maps the
        // same Yandex states, and a status handled there but not here silently waits out a whole
        // reconciliation cycle.
        var eventType = notification.Status.Trim().ToUpperInvariant() switch
        {
            "CAPTURED" => PaymentWebhookEventTypes.Paid,
            "FAILED" or "VOIDED" => PaymentWebhookEventTypes.Canceled,
            "REFUNDED" or "PARTIALLY_REFUNDED" => PaymentWebhookEventTypes.Refunded,
            // PENDING / AUTHORIZED / CONFIRMED are non-terminal: acknowledged without touching Billing.
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
