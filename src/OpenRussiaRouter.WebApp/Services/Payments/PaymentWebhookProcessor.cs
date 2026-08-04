using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Maps a normalised webhook event to the right BillingService gRPC call. Does NOT touch any
/// database — all persistence (payment_event insert, state transition, ledger entry) happens
/// atomically inside BillingService.
/// </summary>
/// <remarks>
/// <para>
/// Event-type mapping (lowercase, as emitted by <c>IPaymentProvider.Parse</c>):
/// </para>
/// <list type="bullet">
///   <item><c>paid</c> → <c>ConfirmPaymentByProvider</c></item>
///   <item><c>canceled</c> / <c>expired</c> / <c>refunded</c> / <c>chargeback</c> → <c>ApplyPaymentEvent</c></item>
///   <item><c>created</c> → no-op (the local row is already Created)</item>
///   <item>anything else → logged and reported as <c>Ignored</c> (no error — provider may emit informational events we don't care about)</item>
/// </list>
/// <para>
/// Idempotency is the responsibility of BillingService: it deduplicates by
/// <c>(provider, provider_event_id)</c> and the ledger idempotency key. Re-delivering the same
/// webhook is safe.
/// </para>
/// </remarks>
public sealed class PaymentWebhookProcessor
{
    private readonly BillingProvider _billing;
    private readonly ILogger<PaymentWebhookProcessor> _logger;

    public PaymentWebhookProcessor(
        BillingProvider billing,
        ILogger<PaymentWebhookProcessor> logger)
    {
        _billing = billing;
        _logger = logger;
    }

    /// <summary>Outcome of processing a single webhook event.</summary>
    public enum ProcessOutcome
    {
        /// <summary>Event was applied (or was a duplicate of an already-applied event).</summary>
        Applied,
        /// <summary>Event was a no-op (e.g. <c>created</c>).</summary>
        Ignored,
        /// <summary>
        /// Billing refused the event for a reason that could clear on its own (provider or FX
        /// outage, missing amount we may be able to fetch next time). Worth a redelivery.
        /// </summary>
        Rejected,
        /// <summary>
        /// Billing refused the event for good: the payment is already in a state that cannot accept
        /// it, or the confirmed amount disagrees with the record. Redelivering the identical event
        /// produces the identical refusal, so the provider must be told to stop.
        /// </summary>
        RejectedPermanent,
    }

    public sealed record ProcessResult(ProcessOutcome Outcome, string? ErrorCode = null);

    /// <summary>
    /// Billing error codes that no redelivery can turn into an acceptance.
    /// </summary>
    /// <remarks>
    /// Payment providers treat any non-2xx as "retry later" — Yandex Pay keeps redelivering for 24h
    /// with backoff. Answering 422 to a verdict that is final buys nothing and turns one dead
    /// payment into a day of noise, so these are acknowledged instead.
    /// <para>
    /// Deliberately absent: <c>payment_not_found</c> and <c>AMOUNT_MISSING</c>. Both can be caused
    /// by something transient — a webhook overtaking the local row, or the amount lookup against the
    /// provider API failing — and dropping a payment event is far worse than logging a retry.
    /// </para>
    /// </remarks>
    private static bool IsPermanentRejection(string? errorCode) => errorCode is
        ErrorCodes.PaymentInvalidTransition or
        ErrorCodes.PaymentAmountMismatch or
        ErrorCodes.PaymentAlreadyProcessed;

    /// <summary>
    /// Process a verified webhook event. Caller has already done <c>IPaymentProvider.Verify</c>
    /// and <c>IPaymentProvider.Parse</c>; this method only does the Billing-side dispatch.
    /// </summary>
    /// <param name="providerName">Provider key (matches <see cref="IPaymentProvider.Name"/>).</param>
    /// <param name="event">Normalised webhook payload.</param>
    /// <param name="rawPayload">Original raw body, kept by Billing for audit.</param>
    /// <param name="signatureOk">Result of <c>Verify</c> — passed through to Billing for audit.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<ProcessResult> ProcessAsync(
        string providerName,
        PaymentWebhookEvent webhookEvent,
        string rawPayload,
        bool signatureOk,
        CancellationToken ct)
    {
        if (webhookEvent is null) throw new ArgumentNullException(nameof(webhookEvent));
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("providerName is required.", nameof(providerName));

        var eventType = webhookEvent.EventType.ToLowerInvariant();
        switch (eventType)
        {
            case PaymentWebhookEventTypes.Paid:
                return await ConfirmAsync(providerName, webhookEvent, rawPayload, signatureOk, ct);

            case PaymentWebhookEventTypes.Canceled:
            case PaymentWebhookEventTypes.Expired:
            case PaymentWebhookEventTypes.Refunded:
            case PaymentWebhookEventTypes.Chargeback:
                return await ApplyEventAsync(providerName, eventType, webhookEvent, rawPayload, signatureOk, ct);

            case PaymentWebhookEventTypes.Created:
                _logger.LogDebug(
                    "Webhook event 'created' for provider={Provider} providerRef={ProviderRef} order={Order} — no-op.",
                    providerName, webhookEvent.ProviderPaymentId, webhookEvent.OrderId);
                return new ProcessResult(ProcessOutcome.Ignored);

            default:
                _logger.LogWarning(
                    "Unknown webhook event_type='{EventType}' from provider={Provider} — ignoring.",
                    eventType, providerName);
                return new ProcessResult(ProcessOutcome.Ignored);
        }
    }

    private async Task<ProcessResult> ConfirmAsync(
        string providerName,
        PaymentWebhookEvent evt,
        string rawPayload,
        bool signatureOk,
        CancellationToken ct)
    {
        if (!evt.ConfirmedAmount.HasValue || string.IsNullOrWhiteSpace(evt.ConfirmedCurrency))
        {
            _logger.LogError(
                "Webhook 'paid' from {Provider} missing amount/currency (providerRef={ProviderRef} order={Order}).",
                providerName, evt.ProviderPaymentId, evt.OrderId);
            return new ProcessResult(ProcessOutcome.Rejected, "AMOUNT_MISSING");
        }

        var result = await _billing.ConfirmPaymentByProviderAsync(
            provider: providerName,
            providerPaymentId: evt.ProviderPaymentId,
            orderId: evt.OrderId,
            confirmedAmount: evt.ConfirmedAmount.Value,
            confirmedCurrency: evt.ConfirmedCurrency!,
            providerEventId: evt.ProviderEventId,
            rawPayload: rawPayload,
            signatureOk: signatureOk,
            ct);

        if (!result.Confirmed && !string.IsNullOrEmpty(result.ErrorCode))
        {
            // Idempotent re-delivery of an already-Paid payment returns Confirmed=true with the
            // original ledger_entry_id. A Confirmed=false with no error code is unexpected — log it.
            _logger.LogWarning(
                "ConfirmPaymentByProvider returned Confirmed=false for provider={Provider} " +
                "providerRef={ProviderRef} order={Order} eventId={EventId} errorCode={ErrorCode}.",
                providerName, evt.ProviderPaymentId, evt.OrderId, evt.ProviderEventId, result.ErrorCode);
            return new ProcessResult(
                IsPermanentRejection(result.ErrorCode) ? ProcessOutcome.RejectedPermanent : ProcessOutcome.Rejected,
                result.ErrorCode);
        }

        // Grant first top-up bonus if applicable (best-effort)
        if (result.Confirmed && !string.IsNullOrEmpty(evt.OrderId))
        {
            try
            {
                await _billing.GrantFirstTopUpBonusAsync(evt.OrderId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to grant first top-up bonus for payment {PaymentId}", evt.OrderId);
            }
        }

        return new ProcessResult(ProcessOutcome.Applied);
    }

    private async Task<ProcessResult> ApplyEventAsync(
        string providerName,
        string eventType,
        PaymentWebhookEvent evt,
        string rawPayload,
        bool signatureOk,
        CancellationToken ct)
    {
        var result = await _billing.ApplyPaymentEventAsync(
            provider: providerName,
            providerPaymentId: evt.ProviderPaymentId,
            orderId: evt.OrderId,
            eventType: eventType,
            reason: evt.Reason,
            confirmedAmount: evt.ConfirmedAmount,
            confirmedCurrency: evt.ConfirmedCurrency,
            providerEventId: evt.ProviderEventId,
            rawPayload: rawPayload,
            signatureOk: signatureOk,
            ct);

        if (!result.Applied && !string.IsNullOrEmpty(result.ErrorCode))
        {
            _logger.LogWarning(
                "ApplyPaymentEvent returned Applied=false for provider={Provider} event={Event} " +
                "providerRef={ProviderRef} order={Order} eventId={EventId} errorCode={ErrorCode}.",
                providerName, eventType, evt.ProviderPaymentId, evt.OrderId, evt.ProviderEventId, result.ErrorCode);
            return new ProcessResult(
                IsPermanentRejection(result.ErrorCode) ? ProcessOutcome.RejectedPermanent : ProcessOutcome.Rejected,
                result.ErrorCode);
        }

        return new ProcessResult(ProcessOutcome.Applied);
    }
}
