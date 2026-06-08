using System.Globalization;

namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>
/// WebApp-side projection of <c>billing.payments</c>. Mirrors only the fields WebApp needs for
/// UI display and checkout orchestration. The authoritative record lives in BillingService.
/// </summary>
public sealed record PaymentInfo(
    string Id,
    string UserId,
    string Provider,
    string? ProviderPaymentId,
    string OrderId,
    decimal DisplayAmount,
    string DisplayCurrency,
    decimal ProviderAmount,
    string ProviderCurrency,
    decimal CreditedUsdAmount,
    decimal FxRate,
    string FxSource,
    DateTimeOffset FxAtUtc,
    string Status,
    string? CancelReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? CanceledAtUtc);

/// <summary>
/// Result of <c>CreatePayment</c>: the internal payment id (= order id sent to the provider)
/// and the credited USD amount that Billing computed from its trusted FX quote.
/// </summary>
public sealed record CreatedPaymentInfo(
    string PaymentId,
    string OrderId,
    decimal CreditedUsdAmount,
    decimal FxRate);

/// <summary>
/// Result of <c>ConfirmPaymentByProvider</c>: whether the transition was applied, the ledger
/// entry id (if any), and the resulting balance. Idempotent re-confirms return <c>Confirmed=true</c>
/// with the original ledger entry id.
/// </summary>
public sealed record ConfirmPaymentResult(
    bool Confirmed,
    string? LedgerEntryId,
    decimal? BalanceUsd,
    string? ErrorCode);

/// <summary>
/// Result of <c>ApplyPaymentEvent</c> for non-paid events (canceled/expired/refunded/chargeback).
/// </summary>
public sealed record ApplyPaymentEventResult(
    bool Applied,
    string? LedgerEntryId,
    string? ErrorCode);

internal static class PaymentRecordMapper
{
    public static PaymentInfo ToInfo(OpenRussiaRouter.Contracts.Grpc.Billing.V1.PaymentRecord r)
    {
        return new PaymentInfo(
            Id: r.Id,
            UserId: r.UserId,
            Provider: r.Provider,
            ProviderPaymentId: string.IsNullOrEmpty(r.ProviderPaymentId) ? null : r.ProviderPaymentId,
            OrderId: r.OrderId,
            DisplayAmount: ParseDecimal(r.DisplayAmount),
            DisplayCurrency: r.DisplayCurrency,
            ProviderAmount: ParseDecimal(r.ProviderAmount),
            ProviderCurrency: r.ProviderCurrency,
            CreditedUsdAmount: ParseDecimal(r.CreditedUsdAmount),
            FxRate: ParseDecimal(r.FxRate),
            FxSource: r.FxSource,
            FxAtUtc: ParseDate(r.FxAtUtc),
            Status: r.Status,
            CancelReason: string.IsNullOrEmpty(r.CancelReason) ? null : r.CancelReason,
            CreatedAtUtc: ParseDate(r.CreatedAtUtc),
            ExpiresAtUtc: ParseNullableDate(r.ExpiresAtUtc),
            ConfirmedAtUtc: ParseNullableDate(r.ConfirmedAtUtc),
            CanceledAtUtc: ParseNullableDate(r.CanceledAtUtc));
    }

    private static decimal ParseDecimal(string s) =>
        string.IsNullOrEmpty(s) ? 0m : decimal.Parse(s, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string s) =>
        string.IsNullOrEmpty(s) ? DateTimeOffset.MinValue : DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseNullableDate(string s) =>
        string.IsNullOrEmpty(s) ? null : DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
}
