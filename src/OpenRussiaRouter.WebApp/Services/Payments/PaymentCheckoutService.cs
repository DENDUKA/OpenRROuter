using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Support;

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
    private readonly AuthProvider? _auth;
    private readonly ISupportBotNotifier? _botNotifier;

    public PaymentCheckoutService(
        BillingProvider billing,
        PaymentProviderRegistry registry,
        ILogger<PaymentCheckoutService> logger,
        AuthProvider? auth = null,
        ISupportBotNotifier? botNotifier = null)
    {
        _billing = billing;
        _registry = registry;
        _logger = logger;
        _auth = auth;
        _botNotifier = botNotifier;
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
    /// <b>Provider currency mapping.</b> A provider that pins <see cref="IPaymentProvider.SettlementCurrency"/>
    /// (an СБП aggregator can only charge rubles) gets the display amount converted into that
    /// currency via Billing's own FX quote. Otherwise provider currency = display currency.
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

        // An СБП aggregator can only charge in rubles, so a provider may pin its settlement currency.
        // Convert here rather than in the UI: Billing derives credited_usd from provider_amount and
        // later compares it against the amount the provider confirms by webhook, so the two must be
        // the same number. A user paying in RUB while Billing recorded USD would be charged, then hit
        // the payment_amount_mismatch anomaly and never get credited.
        var providerCurrency = displayCurrency;
        var providerAmount = displayAmount;

        var settlementCurrency = provider.SettlementCurrency;
        if (settlementCurrency is not null &&
            !string.Equals(settlementCurrency, displayCurrency, StringComparison.OrdinalIgnoreCase))
        {
            providerAmount = await ConvertAsync(displayAmount, displayCurrency, settlementCurrency, ct);
            providerCurrency = settlementCurrency;

            _logger.LogInformation(
                "Checkout converted {DisplayAmount} {DisplayCurrency} to {ProviderAmount} {ProviderCurrency} for provider {Provider}.",
                displayAmount, displayCurrency, providerAmount, providerCurrency, provider.Name);
        }

        // Determine if first top-up bonus applies
        decimal? bonusPercent = null;
        try
        {
            var bonusEnabledRaw = await _billing.GetPlatformSettingAsync(
                PlatformSettingKeys.PromotionsFirstTopUpEnabled, ct);
            if (bool.TryParse(bonusEnabledRaw, out var bonusEnabled) && bonusEnabled)
            {
                var hasPaidBefore = await _billing.HasUserHadPaidTopUpAsync(userId, ct);
                if (!hasPaidBefore)
                {
                    var percentRaw = await _billing.GetPlatformSettingAsync(
                        PlatformSettingKeys.PromotionsFirstTopUpPercent, ct);
                    if (decimal.TryParse(percentRaw, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 0)
                    {
                        bonusPercent = pct;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load first top-up bonus setting. UserId={UserId}", userId);
        }

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
            bonusPercent,
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
                    Description: $"Top-up {displayAmount} {displayCurrency}",
                    UserId: userId),
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

        // Notify bot about created payment (best-effort)
        try
        {
            if (_botNotifier is not null)
            {
                string email = "";
                if (!string.IsNullOrEmpty(userId) && _auth is not null)
                {
                    var user = await _auth.GetUserAsync(userId, ct);
                    email = user?.Email ?? "";
                }

                if (!string.IsNullOrEmpty(email))
                {
                    var providerDisplay = provider.Name.Equals("rollypay", StringComparison.OrdinalIgnoreCase)
                        ? "Rollypay (СБП)"
                        : provider.Name.Equals("yandexpay", StringComparison.OrdinalIgnoreCase)
                            ? "Yandex Pay"
                            : provider.Name;

                    _botNotifier.NotifyPaymentCreated(
                        email,
                        providerAmount,
                        created.CreditedUsdAmount,
                        providerDisplay,
                        created.PaymentId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify bot about created payment {PaymentId}", created.PaymentId);
        }

        return new CheckoutResult(
            PaymentId: created.PaymentId,
            PayUrl: providerCreated.PayUrl,
            CreditedUsdAmount: created.CreditedUsdAmount,
            ProviderCurrency: providerCurrency,
            ProviderAmount: providerAmount);
    }

    /// <summary>Convert an amount between two currencies using BillingService's trusted quotes.</summary>
    /// <remarks>
    /// <para>Both legs come from Billing rather than the WebApp's CBR snapshot: Billing recomputes
    /// <c>credited_usd</c> from the converted amount, and a rate that disagreed with its own would
    /// credit the user something other than what the checkout charged.</para>
    /// <para>Billing quotes each currency against USD with its FX markup already folded in, so the
    /// ratio of two quotes cancels the markup out — it stays applied exactly once, when Billing
    /// credits the payment, instead of being charged twice on a cross-currency top-up.</para>
    /// </remarks>
    private async Task<decimal> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        CancellationToken ct)
    {
        var fromRate = await UnitsPerUsdAsync(fromCurrency, ct);
        var toRate = await UnitsPerUsdAsync(toCurrency, ct);

        if (fromRate <= 0 || toRate <= 0)
        {
            throw new InvalidOperationException(
                $"BillingService returned a non-positive FX rate ({fromCurrency}={fromRate}, {toCurrency}={toRate}); " +
                "refusing to build a checkout amount from it.");
        }

        // Round to minor units: providers take the amount as a 2-decimal string, and Billing has to
        // record the exact value it will later be asked to confirm.
        var converted = decimal.Round(amount * toRate / fromRate, 2, MidpointRounding.AwayFromZero);

        if (converted <= 0)
        {
            throw new InvalidOperationException(
                $"Converting {amount} {fromCurrency} to {toCurrency} rounded down to {converted}.");
        }

        return converted;
    }

    /// <summary>Units of <paramref name="currency"/> per 1 USD, per BillingService's quote.</summary>
    private async Task<decimal> UnitsPerUsdAsync(string currency, CancellationToken ct)
    {
        var (rate, _, _, _) = await _billing.GetFxQuoteAsync(currency, ct);
        return rate;
    }
}
