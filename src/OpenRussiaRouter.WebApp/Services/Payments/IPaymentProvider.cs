using Microsoft.AspNetCore.Http;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Provider-agnostic payment facade. Each concrete payment system (mock, SBP aggregator, etc.)
/// implements this interface and is registered in <see cref="PaymentProviderRegistry"/>.
/// WebApp's webhook/checkout/reconciliation code talks only to this seam — never to a concrete
/// provider class.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Stable lowercase identifier ("mock", "rollypay", ...). Matches the URL segment in <c>/webhook/{provider}</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The only currency this provider can charge in (e.g. <c>"RUB"</c> for an СБП aggregator), or
    /// null when it accepts whatever the user picked.
    /// </summary>
    /// <remarks>
    /// When set, <see cref="PaymentCheckoutService"/> converts the display amount into this currency
    /// before Billing records the payment, so <c>provider_amount</c>/<c>provider_currency</c> always
    /// match what the provider will actually charge and later confirm by webhook. Converting in the
    /// UI instead would let the two drift apart and trip Billing's amount-mismatch anomaly.
    /// </remarks>
    string? SettlementCurrency { get; }

    /// <summary>
    /// Create a payment at the provider. <paramref name="request.OrderId"/> is the internal
    /// <c>payment_id</c> from BillingService — passed to the provider so that early webhooks
    /// (arriving before <c>AttachProviderRef</c>) can still resolve the local payment.
    /// </summary>
    Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct);

    /// <summary>
    /// Verify the raw webhook payload (signature / shared secret). MUST be called on the raw
    /// body string before any JSON parsing — normalisation breaks HMAC.
    /// </summary>
    WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers);

    /// <summary>
    /// Parse the raw webhook payload into a normalised event. Called only after a successful
    /// <see cref="Verify"/>. BillingService is provider-agnostic and consumes only the
    /// normalised fields — it never re-parses <c>raw_payload</c>.
    /// </summary>
    PaymentWebhookEvent Parse(string rawBody);

    /// <summary>
    /// Same as <see cref="Parse(string)"/> for providers whose notification body does not identify
    /// our order and which carry that in the webhook URL query instead (Robokassa ResultUrl2).
    /// </summary>
    PaymentWebhookEvent Parse(string rawBody, IQueryCollection query) => Parse(rawBody);

    /// <summary>
    /// Fetch the current status of a payment at the provider. Used by the reconciliation worker
    /// to close dangling <c>Created</c> payments when webhooks were missed.
    /// </summary>
    Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct);

    /// <summary>
    /// Plain-text acknowledgement the provider expects in the webhook response body (e.g. Robokassa
    /// wants <c>OK{InvId}</c>), or null to answer with the default JSON.
    /// </summary>
    string? WebhookAckText(PaymentWebhookEvent evt) => null;
}
