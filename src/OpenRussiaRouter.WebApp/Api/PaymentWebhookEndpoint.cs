using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// Minimal-API endpoint for <c>POST /webhook/{provider}</c>. Hosts the verify-then-parse-then-process
/// pipeline; persistence is delegated to <see cref="PaymentWebhookProcessor"/> (which in turn
/// delegates to BillingService).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hardening (per implementation plan §3):</b>
/// </para>
/// <list type="bullet">
///   <item>Body size capped at 64 KB — anything larger is rejected before any parsing.</item>
///   <item>Raw body read as string ONCE, BEFORE any JSON parsing. JSON normalisation breaks HMAC signatures.</item>
///   <item><c>IPaymentProvider.Verify</c> runs on the raw string; failure → 401.</item>
///   <item>Rate-limited via the <c>payment-webhook</c> policy configured in <c>Program.cs</c>.</item>
/// </list>
/// <para>
/// The endpoint is anonymous (no cookie auth) — providers cannot authenticate as users. Trust
/// comes from the provider's signature in <c>Verify</c>.
/// </para>
/// </remarks>
public static class PaymentWebhookEndpoint
{
    /// <summary>Hard cap on webhook body size. Providers that exceed this are rejected outright.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>Rate-limit policy name applied to this endpoint.</summary>
    public const string RateLimitPolicy = "payment-webhook";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhook/{provider}", HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        string provider,
        PaymentProviderRegistry registry,
        PaymentWebhookProcessor processor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("PaymentWebhookEndpoint");

        if (string.IsNullOrWhiteSpace(provider))
        {
            return Results.BadRequest(new { error = "provider segment is required" });
        }

        var impl = registry.TryGet(provider);
        if (impl is null)
        {
            logger.LogWarning("Webhook received for unknown provider '{Provider}'.", provider);
            return Results.NotFound(new { error = "unknown provider" });
        }

        // Cap body size before reading. ContentLength may be missing (chunked) — fall back to
        // reading with a capped stream.
        if (http.Request.ContentLength is long len && len > MaxBodyBytes)
        {
            logger.LogWarning(
                "Webhook from {Provider} rejected: Content-Length {Length} exceeds {Max}.",
                provider, len, MaxBodyBytes);
            return Results.Problem(
                title: "Webhook payload too large",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        string rawBody;
        try
        {
            using var reader = new StreamReader(
                new LimitedStream(http.Request.Body, MaxBodyBytes),
                leaveOpen: true);
            rawBody = await reader.ReadToEndAsync(ct);
        }
        catch (PayloadTooLargeException)
        {
            logger.LogWarning(
                "Webhook from {Provider} rejected: streamed body exceeds {Max}.",
                provider, MaxBodyBytes);
            return Results.Problem(
                title: "Webhook payload too large",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (rawBody.Length == 0)
        {
            logger.LogWarning("Webhook from {Provider} rejected: empty body.", provider);
            return Results.BadRequest(new { error = "empty body" });
        }

        // Verify on the raw body BEFORE any parsing. JSON normalisation breaks HMAC.
        var verification = impl.Verify(rawBody, http.Request.Headers);
        if (verification != WebhookVerificationResult.Valid)
        {
            logger.LogWarning(
                "Webhook from {Provider} failed signature verification (providerRef hint unknown).",
                provider);
            return Results.Unauthorized();
        }

        PaymentWebhookEvent evt;
        try
        {
            evt = impl.Parse(rawBody, http.Request.Query);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Webhook from {Provider} failed to parse: {Message}",
                provider, ex.Message);
            return Results.BadRequest(new { error = "malformed payload" });
        }

        var result = await processor.ProcessAsync(
            providerName: impl.Name,
            webhookEvent: evt,
            rawPayload: rawBody,
            signatureOk: true,
            ct);

        // Some providers (Robokassa: "OK{InvId}") only accept a provider-specific plain-text ack.
        var ack = impl.WebhookAckText(evt);

        return result.Outcome switch
        {
            PaymentWebhookProcessor.ProcessOutcome.Applied =>
                ack is null ? Results.Ok(new { status = "ok" }) : Results.Text(ack),
            PaymentWebhookProcessor.ProcessOutcome.Ignored =>
                ack is null ? Results.Ok(new { status = "ignored" }) : Results.Text(ack),
            // Final verdict: acknowledge so the provider stops redelivering an event we will never
            // accept. The reason stays visible in the body and in the processor's warning.
            PaymentWebhookProcessor.ProcessOutcome.RejectedPermanent =>
                ack is null ? Results.Ok(new { status = "ignored", error = result.ErrorCode }) : Results.Text(ack),
            PaymentWebhookProcessor.ProcessOutcome.Rejected => Results.Problem(
                title: "Webhook rejected by billing",
                detail: result.ErrorCode,
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(),
        };
    }

}
