using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Owner of the create-checkout flow. UI pages (TS-007) call ONLY this service — never
/// <c>BillingProvider.CreatePayment</c> or <c>AttachProviderRef</c> directly.
/// </summary>
/// <remarks>
/// <para>Flow:</para>
/// <list type="number">
///   <item><c>BillingProvider.CreatePayment</c> — Billing creates the row, computes <c>credited_usd</c> from its trusted FX quote, returns <c>payment_id</c> (= <c>order_id</c>).</item>
///   <item><c>IPaymentProvider.CreateAsync(order_id=payment_id)</c> — provider returns its own payment id and a pay URL.</item>
///   <item><c>BillingProvider.AttachProviderRef</c> — atomically attach the provider-side id + pay URL + expiry.</item>
/// </list>
/// <para>
/// If step 3 fails (e.g. payment expired meanwhile), the provider-side payment is orphaned —
/// that is acceptable: it has no money yet (the user hasn't paid), and reconciliation will
/// eventually observe the local row as <c>Created</c>→<c>Expired</c> and clean up.
/// </para>
/// </remarks>
public sealed class PaymentCheckoutService
{
    private readonly BillingProvider _billing;
    private readonly PaymentProviderRegistry _registry;
    private readonly ILogger<PaymentCheckoutService> _logger;

    public PaymentCheckoutService(
        BillingProvider billing,
        PaymentProviderRegistry registry,
        ILogger<PaymentCheckoutService> logger)
    {
        _billing = billing;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Result of <see cref="CreateAsync"/>: the URL the user should be redirected to and the
    /// internal payment id (for status polling / receipts).
    /// </summary>
    public sealed record CheckoutResult(string PaymentId, string PayUrl, decimal CreditedUsdAmount, string ProviderCurrency, decimal ProviderAmount);

    /// <summary>
    /// Orchestrate the full create-checkout flow.
    /// </summary>
    /// <param name="userId">Auth user id (from cookie auth).</param>
    /// <param name="displayAmount">What the user typed in the form.</param>
    /// <param name="displayCurrency">Currency of the form input (RUB/USD/EUR).</param>
    /// <param name="providerName">Provider key (matches <see cref="IPaymentProvider.Name"/>). When null, the registry's active provider is used.</param>
    /// <param name="clientToken">Idempotency token for <c>CreatePayment</c>. UI should reuse the same token on retries.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// <para>
    /// <b>Provider currency mapping.</b> Today the provider currency is assumed equal to the
    /// display currency. For a real SBP aggregator this will need a fixed <c>RUB</c> mapping
    /// regardless of display — that logic belongs here, not in the UI.
    /// </para>
    /// </remarks>
    public async Task<CheckoutResult> CreateAsync(
        string userId,
        decimal displayAmount,
        string displayCurrency,
        string? providerName,
        string clientToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (displayAmount <= 0)
        {
            throw new ArgumentException("displayAmount must be positive.", nameof(displayAmount));
        }
        if (string.IsNullOrWhiteSpace(displayCurrency))
        {
            throw new ArgumentException("displayCurrency is required.", nameof(displayCurrency));
        }
        if (string.IsNullOrWhiteSpace(clientToken))
        {
            throw new ArgumentException("clientToken is required for idempotency.", nameof(clientToken));
        }

        var provider = string.IsNullOrWhiteSpace(providerName)
            ? _registry.Active
            : _registry.TryGet(providerName!);

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"Payment provider '{providerName ?? "<active>"}' is not registered. " +
                $"Known: {string.Join(", ", _registry.RegisteredNames)}.");
        }

        // For now provider currency = display currency. Real SBP aggregator will force RUB here.
        var providerCurrency = displayCurrency;
        var providerAmount = displayAmount;

        // 1) Create the payment row in Billing. Billing computes credited_usd from its trusted
        //    FX quote — WebApp never sends a USD amount.
        var created = await _billing.CreatePaymentAsync(
            userId,
            provider: provider.Name,
            providerAmount,
            providerCurrency,
            displayAmount,
            displayCurrency,
            clientToken,
            ct);

        // 2) Create the payment at the provider. Pass the internal payment_id as order_id so
        //    that webhooks arriving before AttachProviderRef can still resolve the local row.
        PaymentCreated providerCreated;
        try
        {
            providerCreated = await provider.CreateAsync(
                new PaymentRequest(
                    OrderId: created.PaymentId,
                    Amount: providerAmount,
                    Currency: providerCurrency,
                    Description: $"Top-up {displayAmount} {displayCurrency}"),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Provider call failed. The Billing row stays in Created — reconciliation will
            // eventually expire it. We do NOT attempt to cancel here because:
            //   - the provider has no payment yet (CreateAsync threw);
            //   - calling ApplyPaymentEvent with "canceled" would race with a possible late
            //     webhook from a partial provider-side creation.
            // The user can safely retry with the same client_token — Billing's idempotency
            // will return the same payment_id and we'll try the provider again.
            _logger.LogError(ex,
                "Provider {Provider} CreateAsync failed for payment {PaymentId}; row stays Created.",
                provider.Name, created.PaymentId);
            throw;
        }

        // 3) Attach the provider-side id + pay URL. If this fails because the payment is no
        //    longer Created (e.g. expired between steps 1 and 3), we surface the error — the
        //    user can retry with a fresh client_token.
        try
        {
            await _billing.AttachProviderRefAsync(
                created.PaymentId,
                providerCreated.ProviderPaymentId,
                providerCreated.PayUrl,
                providerCreated.ExpiresAtUtc,
                ct);
        }
        catch (BillingException ex)
        {
            _logger.LogError(ex,
                "AttachProviderRef failed for payment {PaymentId} (providerRef={ProviderRef}); " +
                "provider-side payment is orphaned.",
                created.PaymentId, providerCreated.ProviderPaymentId);
            throw;
        }

        return new CheckoutResult(
            PaymentId: created.PaymentId,
            PayUrl: providerCreated.PayUrl,
            CreditedUsdAmount: created.CreditedUsdAmount,
            ProviderCurrency: providerCurrency,
            ProviderAmount: providerAmount);
    }
}
