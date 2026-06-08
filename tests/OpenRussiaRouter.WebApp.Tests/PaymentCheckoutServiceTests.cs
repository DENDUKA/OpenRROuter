using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;

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

    public override Task<CreatedPaymentInfo> CreatePaymentAsync(
        string userId, string provider,
        decimal providerAmount, string providerCurrency,
        decimal displayAmount, string displayCurrency,
        string clientToken,
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
}
