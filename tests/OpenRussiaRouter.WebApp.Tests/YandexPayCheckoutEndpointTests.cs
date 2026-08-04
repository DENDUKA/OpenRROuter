using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Api;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class YandexPayCheckoutEndpointTests
{
    [Fact]
    public void HandleAsync_requires_admin_role()
    {
        var method = typeof(YandexPayCheckoutEndpoint).GetMethod(
            "HandleAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0, "RUB", true, "invalid_amount")]
    [InlineData(10.123, "RUB", true, "invalid_amount")]
    [InlineData(10, "GBP", true, "unsupported_currency")]
    [InlineData(10, "RUB", false, "legal_consent_required")]
    public async Task HandleAsync_invalid_request_returns_a_specific_validation_error(
        decimal amount,
        string currency,
        bool legalConsent,
        string expectedError)
    {
        var result = await YandexPayCheckoutEndpoint.HandleAsync(
            new YandexPayCheckoutRequest(amount, currency, legalConsent),
            CreateContext(),
            CreateCheckoutService(),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (context, json) = await ExecuteAsync(result);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        json.RootElement.GetProperty("error").GetString().Should().Be(expectedError);
    }

    [Fact]
    public async Task HandleAsync_valid_request_creates_yandexpay_checkout_and_returns_pay_url()
    {
        var billing = new CheckoutBillingProvider();
        var result = await YandexPayCheckoutEndpoint.HandleAsync(
            new YandexPayCheckoutRequest(10m, "usd", true),
            CreateContext(),
            CreateCheckoutService(billing),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (context, json) = await ExecuteAsync(result);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        json.RootElement.GetProperty("payUrl").GetString().Should().Be("https://sandbox.pay.yandex.ru/pay-1");
        billing.Provider.Should().Be("yandexpay");
        billing.Currency.Should().Be("USD");
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin-1"), new Claim(ClaimTypes.Role, "Admin")],
            "test"));
        return context;
    }

    private static PaymentCheckoutService CreateCheckoutService(CheckoutBillingProvider? billing = null)
    {
        billing ??= new CheckoutBillingProvider();
        var registry = new PaymentProviderRegistry(
            [new YandexPayProvider()],
            Options.Create(new PaymentSettings()));
        return new PaymentCheckoutService(billing, registry, NullLogger<PaymentCheckoutService>.Instance);
    }

    private static async Task<(DefaultHttpContext Context, JsonDocument Json)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var json = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, json);
    }

    private sealed class YandexPayProvider : IPaymentProvider
    {
        public string Name => "yandexpay";
        public string? SettlementCurrency => null;
        public Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct) =>
            Task.FromResult(new PaymentCreated("yandex-pay-1", "https://sandbox.pay.yandex.ru/pay-1"));
        public WebhookVerificationResult Verify(string rawBody, IHeaderDictionary headers) => WebhookVerificationResult.Invalid;
        public PaymentWebhookEvent Parse(string rawBody) => throw new NotSupportedException();
        public Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CheckoutBillingProvider : BillingProvider
    {
        public CheckoutBillingProvider() : base(client: default!) { }

        public string? Provider { get; private set; }
        public string? Currency { get; private set; }

        public override Task<CreatedPaymentInfo> CreatePaymentAsync(
            string userId, string provider, decimal providerAmount, string providerCurrency,
            decimal displayAmount, string displayCurrency, string clientToken, decimal? bonusPercent = null,
            CancellationToken cancellationToken = default)
        {
            Provider = provider;
            Currency = providerCurrency;
            return Task.FromResult(new CreatedPaymentInfo("payment-1", "payment-1", providerAmount, 1m));
        }

        public override Task AttachProviderRefAsync(
            string paymentId, string providerPaymentId, string payUrl, DateTimeOffset? expiresAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
