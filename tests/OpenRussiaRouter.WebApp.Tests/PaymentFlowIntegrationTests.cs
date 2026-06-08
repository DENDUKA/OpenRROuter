using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class PaymentFlowIntegrationTests
{
    [Fact]
    public async Task Mock_checkout_paid_webhook_increases_balance_once()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var billing = factory.Services.GetRequiredService<PaymentFlowFakeBilling>();

        PaymentCheckoutService.CheckoutResult checkout;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PaymentCheckoutService>();
            checkout = await service.CreateAsync(
                userId: "user-1",
                displayAmount: 10m,
                displayCurrency: "USD",
                providerName: "mock",
                clientToken: "checkout-1",
                CancellationToken.None);
        }

        var payload = MockPaymentProvider.BuildWebhookPayload(
            billing.ProviderPaymentId!,
            checkout.PaymentId,
            eventType: "paid",
            amount: 10m,
            currency: "USD",
            providerEventId: "evt-paid-1");

        using var first = await PostWebhookAsync(client, payload);
        using var duplicate = await PostWebhookAsync(client, payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(10m, billing.BalanceUsd);
        Assert.Equal(1, billing.AppliedConfirmations);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BillingProvider>();
                services.AddSingleton<PaymentFlowFakeBilling>();
                services.AddSingleton<BillingProvider>(sp => sp.GetRequiredService<PaymentFlowFakeBilling>());
            });
        });

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/mock")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Mock-Signature", "dev");
        return await client.SendAsync(request);
    }

    private sealed class PaymentFlowFakeBilling : BillingProvider
    {
        private readonly HashSet<string> _processedEvents = new(StringComparer.Ordinal);
        private string? _paymentId;
        private decimal _creditedUsd;

        public PaymentFlowFakeBilling() : base(client: default!) { }

        public string? ProviderPaymentId { get; private set; }
        public decimal BalanceUsd { get; private set; }
        public int AppliedConfirmations { get; private set; }

        public override Task<CreatedPaymentInfo> CreatePaymentAsync(
            string userId,
            string provider,
            decimal providerAmount,
            string providerCurrency,
            decimal displayAmount,
            string displayCurrency,
            string clientToken,
            CancellationToken cancellationToken = default)
        {
            _paymentId ??= "pay-integration-1";
            _creditedUsd = providerAmount;
            return Task.FromResult(new CreatedPaymentInfo(_paymentId, _paymentId, _creditedUsd, 1m));
        }

        public override Task AttachProviderRefAsync(
            string paymentId,
            string providerPaymentId,
            string payUrl,
            DateTimeOffset? expiresAtUtc,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(_paymentId, paymentId);
            ProviderPaymentId = providerPaymentId;
            return Task.CompletedTask;
        }

        public override Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
            string provider,
            string? providerPaymentId,
            string? orderId,
            decimal confirmedAmount,
            string confirmedCurrency,
            string? providerEventId,
            string rawPayload,
            bool signatureOk,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("mock", provider);
            Assert.Equal(ProviderPaymentId, providerPaymentId);
            Assert.Equal(_paymentId, orderId);
            Assert.Equal(10m, confirmedAmount);
            Assert.Equal("USD", confirmedCurrency);
            Assert.True(signatureOk);

            if (_processedEvents.Add(providerEventId!))
            {
                BalanceUsd += _creditedUsd;
                AppliedConfirmations++;
            }

            return Task.FromResult(new ConfirmPaymentResult(true, "ledger-1", BalanceUsd, null));
        }
    }
}
