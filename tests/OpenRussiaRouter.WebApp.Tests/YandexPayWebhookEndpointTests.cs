using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Api;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class YandexPayWebhookEndpointTests
{
    [Theory]
    [InlineData("CAPTURED", "paid")]
    [InlineData("FAILED", "canceled")]
    [InlineData("REFUNDED", "refunded")]
    public async Task HandleAsync_verified_terminal_status_is_processed(string status, string expectedEventType)
    {
        var billing = new WebhookBillingProvider();
        var result = await YandexPayWebhookEndpoint.HandleAsync(
            CreateContext("signed.jwt"),
            new Verifier(new YandexPayWebhookNotification(
                "yandex-payment-1", "payment-1", status, 100m, "RUB", "event-1", null)),
            new PaymentWebhookProcessor(billing, NullLogger<PaymentWebhookProcessor>.Instance),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var response = await ExecuteAsync(result);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        billing.EventType.Should().Be(expectedEventType);
        billing.Provider.Should().Be("yandexpay");
        billing.RawPayload.Should().Be("signed.jwt");
    }

    [Fact]
    public async Task HandleAsync_invalid_jwt_is_rejected_before_any_notification_is_processed()
    {
        var billing = new WebhookBillingProvider();
        var result = await YandexPayWebhookEndpoint.HandleAsync(
            CreateContext("not-a-jwt"),
            new Verifier(null),
            new PaymentWebhookProcessor(billing, NullLogger<PaymentWebhookProcessor>.Instance),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var response = await ExecuteAsync(result);

        response.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        billing.EventType.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_unknown_verified_status_is_acknowledged_without_billing_call()
    {
        var billing = new WebhookBillingProvider();
        var result = await YandexPayWebhookEndpoint.HandleAsync(
            CreateContext("signed.jwt"),
            new Verifier(new YandexPayWebhookNotification(null, "payment-1", "PENDING", null, null, "event-1", null)),
            new PaymentWebhookProcessor(billing, NullLogger<PaymentWebhookProcessor>.Instance),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var response = await ExecuteAsync(result);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        billing.EventType.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_body_larger_than_limit_is_rejected_without_verification()
    {
        var verifier = new Verifier(new YandexPayWebhookNotification(null, null, "CAPTURED", 1m, "RUB", "event-1", null));
        var result = await YandexPayWebhookEndpoint.HandleAsync(
            CreateContext(new string('a', YandexPayWebhookEndpoint.MaxBodyBytes + 1)),
            verifier,
            new PaymentWebhookProcessor(new WebhookBillingProvider(), NullLogger<PaymentWebhookProcessor>.Instance),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var response = await ExecuteAsync(result);

        response.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        verifier.Called.Should().BeFalse();
    }

    private static DefaultHttpContext CreateContext(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        return context;
    }

    private static async Task<DefaultHttpContext> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await result.ExecuteAsync(context);
        return context;
    }

    private sealed class Verifier(YandexPayWebhookNotification? notification) : IYandexPayWebhookVerifier
    {
        public bool Called { get; private set; }
        public Task<YandexPayWebhookNotification?> VerifyAsync(string rawJwt, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(notification);
        }
    }

    private sealed class WebhookBillingProvider : BillingProvider
    {
        public WebhookBillingProvider() : base(client: default!) { }

        public string? Provider { get; private set; }
        public string? EventType { get; private set; }
        public string? RawPayload { get; private set; }

        public override Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
            string provider, string? providerPaymentId, string? orderId, decimal confirmedAmount,
            string confirmedCurrency, string? providerEventId, string rawPayload, bool signatureOk,
            CancellationToken cancellationToken = default)
        {
            Provider = provider;
            EventType = PaymentWebhookEventTypes.Paid;
            RawPayload = rawPayload;
            return Task.FromResult(new ConfirmPaymentResult(true, "ledger-1", 1m, null));
        }

        public override Task<ApplyPaymentEventResult> ApplyPaymentEventAsync(
            string provider, string? providerPaymentId, string? orderId, string eventType, string? reason,
            decimal? confirmedAmount, string? confirmedCurrency, string? providerEventId, string rawPayload,
            bool signatureOk, CancellationToken cancellationToken = default)
        {
            Provider = provider;
            EventType = eventType;
            RawPayload = rawPayload;
            return Task.FromResult(new ApplyPaymentEventResult(true, null, null));
        }
    }
}
