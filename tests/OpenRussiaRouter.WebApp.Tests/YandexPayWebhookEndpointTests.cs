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
    [InlineData("VOIDED", "canceled")]
    [InlineData("REFUNDED", "refunded")]
    [InlineData("PARTIALLY_REFUNDED", "refunded")]
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
        // Yandex Pay documents {"status":"success"} as the acknowledgement body.
        ReadBody(response).Should().Be("""{"status":"success"}""");
        billing.EventType.Should().Be(expectedEventType);
        billing.Provider.Should().Be("yandexpay");
        billing.RawPayload.Should().Be("signed.jwt");
    }

    [Fact]
    public async Task HandleAsync_transient_billing_rejection_answers_422_with_reason_code()
    {
        var billing = new WebhookBillingProvider { RejectionCode = "payment_not_found" };
        var result = await YandexPayWebhookEndpoint.HandleAsync(
            CreateContext("signed.jwt"),
            new Verifier(new YandexPayWebhookNotification(
                "yandex-payment-1", "payment-1", "CAPTURED", 100m, "RUB", "event-1", null)),
            new PaymentWebhookProcessor(billing, NullLogger<PaymentWebhookProcessor>.Instance),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var response = await ExecuteAsync(result);

        response.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReadBody(response).Should().Be("""{"reasonCode":"ORDER_NOT_FOUND"}""");
    }

    [Fact]
    public async Task HandleAsync_permanent_billing_rejection_is_acknowledged_to_stop_redelivery()
    {
        // Yandex Pay retries any non-2xx for 24h. A verdict no redelivery can change is acked
        // instead; the reason only goes to the log, since the ack body has no field for it.
        var billing = new WebhookBillingProvider { RejectionCode = "payment_already_processed" };
        var result = await YandexPayWebhookEndpoint.HandleAsync(
            CreateContext("signed.jwt"),
            new Verifier(new YandexPayWebhookNotification(
                "yandex-payment-1", "payment-1", "CAPTURED", 100m, "RUB", "event-1", null)),
            new PaymentWebhookProcessor(billing, NullLogger<PaymentWebhookProcessor>.Instance),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var response = await ExecuteAsync(result);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ReadBody(response).Should().Be("""{"status":"success"}""");
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
        ReadBody(response).Should().Be("""{"reasonCode":"UNAUTHORIZED"}""");
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
        ReadBody(response).Should().Be("""{"status":"success"}""");
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
        ReadBody(response).Should().Be("""{"reasonCode":"UNAUTHORIZED"}""");
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

    private static string ReadBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
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

        /// <summary>When set, Billing refuses the event with this error code.</summary>
        public string? RejectionCode { get; init; }

        public override Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
            string provider, string? providerPaymentId, string? orderId, decimal confirmedAmount,
            string confirmedCurrency, string? providerEventId, string rawPayload, bool signatureOk,
            CancellationToken cancellationToken = default)
        {
            Provider = provider;
            EventType = PaymentWebhookEventTypes.Paid;
            RawPayload = rawPayload;
            return Task.FromResult(RejectionCode is null
                ? new ConfirmPaymentResult(true, "ledger-1", 1m, null)
                : new ConfirmPaymentResult(false, null, null, RejectionCode));
        }

        public override Task<ApplyPaymentEventResult> ApplyPaymentEventAsync(
            string provider, string? providerPaymentId, string? orderId, string eventType, string? reason,
            decimal? confirmedAmount, string? confirmedCurrency, string? providerEventId, string rawPayload,
            bool signatureOk, CancellationToken cancellationToken = default)
        {
            Provider = provider;
            EventType = eventType;
            RawPayload = rawPayload;
            return Task.FromResult(RejectionCode is null
                ? new ApplyPaymentEventResult(true, null, null)
                : new ApplyPaymentEventResult(false, null, RejectionCode));
        }
    }
}
