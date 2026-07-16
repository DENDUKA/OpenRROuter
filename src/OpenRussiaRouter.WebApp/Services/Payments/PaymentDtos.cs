namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Request to create a payment at the provider side.
/// <c>OrderId</c> is the internal <c>payment_id</c> from BillingService — passed to the provider
/// so that webhooks arriving before <c>AttachProviderRef</c> can still resolve the local payment.
/// </summary>
/// <param name="UserId">
/// Internal account id of the payer, for providers that can key their risk scoring on a customer.
/// An opaque identifier only — never an email, name or IP: payments are created server-side, so
/// everything we put here is a deliberate disclosure to a third party.
/// </param>
public sealed record PaymentRequest(
    string OrderId,
    decimal Amount,
    string Currency,
    string Description,
    string? UserId = null);

/// <summary>
/// Result of <c>IPaymentProvider.CreateAsync</c>: provider-side identifier of the payment
/// and a URL the user should be redirected to in order to complete checkout.
/// </summary>
public sealed record PaymentCreated(
    string ProviderPaymentId,
    string PayUrl,
    DateTimeOffset? ExpiresAtUtc = null);

/// <summary>
/// Status of a payment as observed at the provider (used by reconciliation worker).
/// Mirrors the subset of states we actually act on.
/// </summary>
public enum ProviderPaymentStatusType
{
    Unknown = 0,
    Created = 1,
    Pending = 2,
    Paid = 3,
    Canceled = 4,
    Expired = 5,
    Refunded = 6,
    ChargedBack = 7,
}

public sealed record ProviderPaymentStatus(
    ProviderPaymentStatusType Status,
    decimal? ConfirmedAmount,
    string? ConfirmedCurrency,
    string? ProviderEventId,
    string? Reason);

/// <summary>
/// Outcome of <c>IPaymentProvider.Verify</c>. <c>Invalid</c> MUST result in HTTP 401 by the caller.
/// </summary>
public enum WebhookVerificationResult
{
    Valid = 0,
    Invalid = 1,
}

/// <summary>
/// Normalised webhook payload produced by <c>IPaymentProvider.Parse</c>.
/// BillingService is provider-agnostic and never re-parses <c>RawPayload</c> — these fields
/// are the only thing it consumes.
/// </summary>
public sealed record PaymentWebhookEvent(
    string? ProviderPaymentId,
    string? OrderId,
    string EventType,
    decimal? ConfirmedAmount,
    string? ConfirmedCurrency,
    string? ProviderEventId,
    string? Reason);

/// <summary>
/// Canonical event-type strings emitted by <c>IPaymentProvider.Parse</c>.
/// Keep lowercase to match the BillingService contract.
/// </summary>
public static class PaymentWebhookEventTypes
{
    public const string Created = "created";
    public const string Paid = "paid";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string Refunded = "refunded";
    public const string Chargeback = "chargeback";
}
