using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Hand-rolled fake for BillingProvider — overrides only the methods used by PaymentCheckoutService.
/// </summary>
internal sealed class CheckoutFakeBilling : BillingProvider
{
    public CheckoutFakeBilling() : base(client: default!) { }

    public string? CreatedPaymentId { get; set; } = "pay-1";
    public string? CreatedOrderId { get; set; } = "pay-1";
    public decimal CreditedUsd { get; set; } = 1m;
    public decimal FxRate { get; set; } = 1m;

    public int CreateCalls { get; private set; }
    public int AttachCalls { get; private set; }

    public (string paymentId, string providerPaymentId, string payUrl, DateTimeOffset? expiresAt)? LastAttach { get; private set; }
    public (string userId, string provider, decimal providerAmount, string providerCurrency, decimal displayAmount, string displayCurrency, string clientToken)? LastCreate { get; private set; }

    public bool ThrowOnAttach { get; set; }
    public bool ThrowOnCreate { get; set; }

    /// <summary>Units per 1 USD, keyed by currency — mirrors BillingService's quote semantics.</summary>
    public Dictionary<string, decimal> Rates { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1m,
        ["RUB"] = 100m,
        ["EUR"] = 0.5m,
    };

    public List<string> FxQuoteCalls { get; } = new();

    public override Task<(decimal Rate, string FxSource, DateTimeOffset FxAt, string? QuoteId)> GetFxQuoteAsync(
        string providerCurrency,
        CancellationToken cancellationToken = default)
    {
        FxQuoteCalls.Add(providerCurrency);
        if (!Rates.TryGetValue(providerCurrency, out var rate))
        {
            throw new BillingException("UnsupportedCurrency");
        }
        return Task.FromResult((rate, "test", DateTimeOffset.UnixEpoch, (string?)"test-quote"));
    }

    public override Task<CreatedPaymentInfo> CreatePaymentAsync(
        string userId, string provider,
        decimal providerAmount, string providerCurrency,
        decimal displayAmount, string displayCurrency,
        string clientToken,
        decimal? bonusPercent = null,
        CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        LastCreate = (userId, provider, providerAmount, providerCurrency, displayAmount, displayCurrency, clientToken);
        if (ThrowOnCreate) throw new BillingException("FxQuoteUnavailable");
        return Task.FromResult(new CreatedPaymentInfo(
            PaymentId: CreatedPaymentId!,
            OrderId: CreatedOrderId!,
            CreditedUsdAmount: CreditedUsd,
            FxRate: FxRate));
    }

    public override Task AttachProviderRefAsync(
        string paymentId, string providerPaymentId, string payUrl,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        AttachCalls++;
        LastAttach = (paymentId, providerPaymentId, payUrl, expiresAtUtc);
        if (ThrowOnAttach) throw new BillingException("PaymentInvalidTransition");
        return Task.CompletedTask;
    }
}

/// <summary>Stub provider that keeps the last <see cref="PaymentRequest"/> it was handed.</summary>
internal sealed class CapturingProvider(string name) : IPaymentProvider
{
    public PaymentRequest? LastRequest { get; private set; }

    public string Name => name;
    public string? SettlementCurrency => null;

    public Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(new PaymentCreated($"cap_{request.OrderId}", $"/pay/{request.OrderId}"));
    }

    public WebhookVerificationResult Verify(string rawBody, Microsoft.AspNetCore.Http.IHeaderDictionary headers)
        => WebhookVerificationResult.Valid;
    public PaymentWebhookEvent Parse(string rawBody) => throw new NotImplementedException();
    public Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct)
        => Task.FromResult(new ProviderPaymentStatus(ProviderPaymentStatusType.Created, null, null, null, null));
}

public class PaymentCheckoutServiceTests
{
    private static PaymentCheckoutService CreateService(
        CheckoutFakeBilling billing,
        string activeProviderName,
        params IPaymentProvider[] providers)
    {
        var registry = new PaymentProviderRegistry(
            providers,
            Microsoft.Extensions.Options.Options.Create(new PaymentSettings { ActiveProvider = activeProviderName }));
        return new PaymentCheckoutService(billing, registry, NullLogger<PaymentCheckoutService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_orchestrates_billing_create_provider_create_attach()
    {
        var billing = new CheckoutFakeBilling();
        var provider = new StubProvider("mock");
        var svc = CreateService(billing, "mock", provider);

        var result = await svc.CreateAsync(
            userId: "user-1",
            displayAmount: 100m,
            displayCurrency: "RUB",
            providerName: null,
            clientToken: "idem-1",
            CancellationToken.None);

        billing.CreateCalls.Should().Be(1);
        billing.AttachCalls.Should().Be(1);
        var c = billing.LastCreate!.Value;
        c.userId.Should().Be("user-1");
        c.provider.Should().Be("mock");
        c.providerAmount.Should().Be(100m);
        c.providerCurrency.Should().Be("RUB");
        c.displayAmount.Should().Be(100m);
        c.displayCurrency.Should().Be("RUB");
        c.clientToken.Should().Be("idem-1");
        billing.LastAttach!.Value.paymentId.Should().Be("pay-1");
        billing.LastAttach!.Value.providerPaymentId.Should().NotBeNullOrWhiteSpace();
        result.PaymentId.Should().Be("pay-1");
        result.CreditedUsdAmount.Should().Be(1m);
    }

    [Fact]
    public async Task CreateAsync_uses_active_provider_when_providerName_is_null()
    {
        var billing = new CheckoutFakeBilling();
        var provider = new StubProvider("mock");
        var svc = CreateService(billing, "mock", provider);

        await svc.CreateAsync("user-1", 10m, "USD", providerName: null, clientToken: "t", CancellationToken.None);

        billing.LastCreate!.Value.provider.Should().Be("mock");
        billing.LastCreate!.Value.providerAmount.Should().Be(10m);
        billing.LastCreate!.Value.providerCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task CreateAsync_passes_the_payer_user_id_to_the_provider()
    {
        var billing = new CheckoutFakeBilling();
        var provider = new CapturingProvider("mock");
        var svc = CreateService(billing, "mock", provider);

        await svc.CreateAsync("user-7", 10m, "USD", providerName: null, clientToken: "t", CancellationToken.None);

        provider.LastRequest!.UserId.Should().Be("user-7");
    }

    [Fact]
    public async Task CreateAsync_converts_display_currency_into_provider_settlement_currency()
    {
        var billing = new CheckoutFakeBilling();
        // An СБП aggregator: charges rubles only, whatever the user picked.
        var svc = CreateService(billing, "sbp", new StubProvider("sbp", settlementCurrency: "RUB"));

        var result = await svc.CreateAsync(
            "user-1", displayAmount: 10m, displayCurrency: "USD",
            providerName: null, clientToken: "t", CancellationToken.None);

        // 10 USD at 100 RUB/USD — Billing must record what the provider will actually charge.
        var c = billing.LastCreate!.Value;
        c.providerAmount.Should().Be(1000m);
        c.providerCurrency.Should().Be("RUB");
        // The display leg is preserved untouched for the user-facing history.
        c.displayAmount.Should().Be(10m);
        c.displayCurrency.Should().Be("USD");
        result.ProviderAmount.Should().Be(1000m);
        result.ProviderCurrency.Should().Be("RUB");
    }

    [Fact]
    public async Task CreateAsync_converts_across_two_non_usd_currencies()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "sbp", new StubProvider("sbp", settlementCurrency: "RUB"));

        await svc.CreateAsync(
            "user-1", displayAmount: 20m, displayCurrency: "EUR",
            providerName: null, clientToken: "t", CancellationToken.None);

        // 20 EUR → 40 USD (0.5 EUR per USD) → 4000 RUB.
        billing.LastCreate!.Value.providerAmount.Should().Be(4000m);
        billing.LastCreate!.Value.providerCurrency.Should().Be("RUB");
    }

    [Fact]
    public async Task CreateAsync_skips_conversion_when_display_already_matches_settlement_currency()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "sbp", new StubProvider("sbp", settlementCurrency: "RUB"));

        await svc.CreateAsync(
            "user-1", displayAmount: 500m, displayCurrency: "rub",
            providerName: null, clientToken: "t", CancellationToken.None);

        billing.LastCreate!.Value.providerAmount.Should().Be(500m);
        // No round-trip to Billing for a quote it would only multiply by one.
        billing.FxQuoteCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_leaves_currency_alone_when_provider_pins_none()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "mock", new StubProvider("mock"));

        await svc.CreateAsync(
            "user-1", displayAmount: 10m, displayCurrency: "USD",
            providerName: null, clientToken: "t", CancellationToken.None);

        billing.LastCreate!.Value.providerAmount.Should().Be(10m);
        billing.LastCreate!.Value.providerCurrency.Should().Be("USD");
        billing.FxQuoteCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_does_not_create_a_payment_when_the_fx_quote_is_unavailable()
    {
        var billing = new CheckoutFakeBilling();
        billing.Rates.Remove("RUB");
        var svc = CreateService(billing, "sbp", new StubProvider("sbp", settlementCurrency: "RUB"));

        var act = async () => await svc.CreateAsync(
            "user-1", 10m, "USD", providerName: null, clientToken: "t", CancellationToken.None);

        await act.Should().ThrowAsync<BillingException>();
        // Conversion happens before CreatePayment, so a bad quote leaves no orphaned row behind.
        billing.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_throws_when_no_provider_registered()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "mock"); // no providers passed

        var act = async () => await svc.CreateAsync("user-1", 10m, "USD", providerName: null, clientToken: "t", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public async Task CreateAsync_rejects_zero_amount()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "mock", new StubProvider("mock"));

        var act = async () => await svc.CreateAsync("user-1", 0m, "USD", providerName: null, clientToken: "t", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*displayAmount must be positive*");
    }

    [Fact]
    public async Task CreateAsync_rejects_empty_userId()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "mock", new StubProvider("mock"));

        var act = async () => await svc.CreateAsync("", 10m, "USD", providerName: null, clientToken: "t", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*userId is required*");
    }

    [Fact]
    public async Task CreateAsync_rejects_empty_clientToken()
    {
        var billing = new CheckoutFakeBilling();
        var svc = CreateService(billing, "mock", new StubProvider("mock"));

        var act = async () => await svc.CreateAsync("user-1", 10m, "USD", providerName: null, clientToken: "", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*clientToken is required*");
    }

    [Fact]
    public async Task CreateAsync_notifies_bot_about_created_payment()
    {
        var billing = new CheckoutFakeBilling();
        var provider = new StubProvider("rollypay");
        var registry = new PaymentProviderRegistry(
            [provider],
            Microsoft.Extensions.Options.Options.Create(new PaymentSettings { ActiveProvider = "rollypay" }));
        var auth = new FakeUserAuthProvider("payer@example.com");
        var botNotifier = new RecordingBotNotifier();

        var svc = new PaymentCheckoutService(
            billing,
            registry,
            NullLogger<PaymentCheckoutService>.Instance,
            auth,
            botNotifier);

        var result = await svc.CreateAsync(
            userId: "user-42",
            displayAmount: 1500m,
            displayCurrency: "RUB",
            providerName: "rollypay",
            clientToken: "idem-42",
            CancellationToken.None);

        result.PaymentId.Should().Be("pay-1");
        botNotifier.CreatedPayments.Should().ContainSingle();
        var created = botNotifier.CreatedPayments[0];
        created.Email.Should().Be("payer@example.com");
        created.AmountRub.Should().Be(1500m);
        created.CreditedUsd.Should().Be(1m);
        created.Provider.Should().Be("Rollypay (СБП)");
        created.PaymentId.Should().Be("pay-1");
    }

    private sealed class RecordingBotNotifier : ISupportBotNotifier
    {
        public List<(string Email, decimal AmountRub, decimal CreditedUsd, string Provider, string? PaymentId)> CreatedPayments { get; } = [];

        public void NotifySupportTicket(string email, string subject, string message) { }
        public void NotifyUserRegistered(string email, bool isDuplicate = false, IReadOnlyList<DuplicateAccountInfo>? duplicates = null) { }
        public void NotifyTopUpCompleted(string email, decimal amountRub, decimal creditedUsd, string provider, string? paymentId = null) { }
        public void NotifyPaymentCreated(string email, decimal amountRub, decimal creditedUsd, string provider, string? paymentId = null)
            => CreatedPayments.Add((email, amountRub, creditedUsd, provider, paymentId));
    }

    private sealed class FakeUserAuthProvider : AuthProvider
    {
        private readonly string _email;
        public FakeUserAuthProvider(string email) : base(null!) => _email = email;

        public override Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<UserInfo?>(new UserInfo(
                userId, _email, "user", "active", DateTimeOffset.UtcNow, null, 5, "RUB"));
        }
    }
}
