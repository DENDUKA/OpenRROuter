using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Hand-rolled fake for BillingProvider — we only need a few methods, and pulling in a mock
/// library just for these tests would be overkill. The fake records every call so tests can
/// assert on dispatch behaviour.
/// </summary>
internal sealed class FakeBillingProvider : BillingProvider
{
    public FakeBillingProvider() : base(client: default!) { }

    public int ConfirmCalls { get; private set; }
    public int ApplyCalls { get; private set; }
    public (string provider, string? providerPaymentId, string? orderId, decimal amount, string currency, string? providerEventId, string rawPayload, bool signatureOk)? LastConfirm { get; private set; }
    public (string provider, string? providerPaymentId, string? orderId, string eventType, string? reason, decimal? amount, string? currency, string? providerEventId, string rawPayload, bool signatureOk)? LastApply { get; private set; }

    public ConfirmPaymentResult NextConfirmResult { get; set; } = new(true, "ledger-1", 100m, ErrorCode: null);
    public ApplyPaymentEventResult NextApplyResult { get; set; } = new(true, "ledger-x", ErrorCode: null);

    public override Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
        string provider, string? providerPaymentId, string? orderId,
        decimal confirmedAmount, string confirmedCurrency,
        string? providerEventId, string rawPayload, bool signatureOk,
        CancellationToken cancellationToken = default)
    {
        ConfirmCalls++;
        LastConfirm = (provider, providerPaymentId, orderId, confirmedAmount, confirmedCurrency, providerEventId, rawPayload, signatureOk);
        return Task.FromResult(NextConfirmResult);
    }

    public override Task<ApplyPaymentEventResult> ApplyPaymentEventAsync(
        string provider, string? providerPaymentId, string? orderId,
        string eventType, string? reason,
        decimal? confirmedAmount, string? confirmedCurrency,
        string? providerEventId, string rawPayload, bool signatureOk,
        CancellationToken cancellationToken = default)
    {
        ApplyCalls++;
        LastApply = (provider, providerPaymentId, orderId, eventType, reason, confirmedAmount, confirmedCurrency, providerEventId, rawPayload, signatureOk);
        return Task.FromResult(NextApplyResult);
    }
}

public class PaymentWebhookProcessorTests
{
    private static PaymentWebhookProcessor CreateProcessor(FakeBillingProvider billing)
        => new(billing, NullLogger<PaymentWebhookProcessor>.Instance);

    [Fact]
    public async Task paid_event_calls_ConfirmPaymentByProvider()
    {
        var billing = new FakeBillingProvider();
        var processor = CreateProcessor(billing);
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: "mock_a",
            OrderId: "pay-1",
            EventType: "paid",
            ConfirmedAmount: 100m,
            ConfirmedCurrency: "RUB",
            ProviderEventId: "evt-1",
            Reason: null);

        var result = await processor.ProcessAsync("mock", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Applied);
        billing.ConfirmCalls.Should().Be(1);
        billing.ApplyCalls.Should().Be(0);
        var c = billing.LastConfirm!.Value;
        c.provider.Should().Be("mock");
        c.providerPaymentId.Should().Be("mock_a");
        c.orderId.Should().Be("pay-1");
        c.amount.Should().Be(100m);
        c.currency.Should().Be("RUB");
        c.providerEventId.Should().Be("evt-1");
        c.rawPayload.Should().Be("{}");
        c.signatureOk.Should().Be(true);
    }

    [Fact]
    public async Task paid_event_without_amount_is_rejected()
    {
        var billing = new FakeBillingProvider();
        var processor = CreateProcessor(billing);
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: "mock_a",
            OrderId: "pay-1",
            EventType: "paid",
            ConfirmedAmount: null,
            ConfirmedCurrency: null,
            ProviderEventId: "evt-1",
            Reason: null);

        var result = await processor.ProcessAsync("mock", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Rejected);
        result.ErrorCode.Should().Be("AMOUNT_MISSING");
        billing.ConfirmCalls.Should().Be(0);
        billing.ApplyCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("refunded")]
    [InlineData("chargeback")]
    public async Task non_paid_events_call_ApplyPaymentEvent(string eventType)
    {
        var billing = new FakeBillingProvider();
        var processor = CreateProcessor(billing);
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: "mock_a",
            OrderId: "pay-1",
            EventType: eventType,
            ConfirmedAmount: 100m,
            ConfirmedCurrency: "RUB",
            ProviderEventId: "evt-2",
            Reason: "user cancelled");

        var result = await processor.ProcessAsync("mock", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Applied);
        billing.ApplyCalls.Should().Be(1);
        billing.ConfirmCalls.Should().Be(0);
        var a = billing.LastApply!.Value;
        a.provider.Should().Be("mock");
        a.providerPaymentId.Should().Be("mock_a");
        a.orderId.Should().Be("pay-1");
        a.eventType.Should().Be(eventType);
        a.reason.Should().Be("user cancelled");
        a.amount.Should().Be(100m);
        a.currency.Should().Be("RUB");
        a.providerEventId.Should().Be("evt-2");
        a.rawPayload.Should().Be("{}");
        a.signatureOk.Should().Be(true);
    }

    [Fact]
    public async Task created_event_is_ignored()
    {
        var billing = new FakeBillingProvider();
        var processor = CreateProcessor(billing);
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: "mock_a",
            OrderId: "pay-1",
            EventType: "created",
            ConfirmedAmount: null,
            ConfirmedCurrency: null,
            ProviderEventId: null,
            Reason: null);

        var result = await processor.ProcessAsync("mock", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Ignored);
        billing.ConfirmCalls.Should().Be(0);
        billing.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task unknown_event_type_is_ignored()
    {
        var billing = new FakeBillingProvider();
        var processor = CreateProcessor(billing);
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: null, OrderId: null,
            EventType: "some_informational_event",
            ConfirmedAmount: null, ConfirmedCurrency: null,
            ProviderEventId: null, Reason: null);

        var result = await processor.ProcessAsync("mock", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Ignored);
    }

    [Fact]
    public async Task confirm_returning_Confirmed_false_with_error_is_Rejected()
    {
        var billing = new FakeBillingProvider
        {
            NextConfirmResult = new ConfirmPaymentResult(false, null, null, ErrorCode: "PaymentAmountMismatch")
        };
        var processor = CreateProcessor(billing);
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: "mock_a", OrderId: "pay-1",
            EventType: "paid",
            ConfirmedAmount: 1m, ConfirmedCurrency: "USD",
            ProviderEventId: "e", Reason: null);

        var result = await processor.ProcessAsync("mock", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Rejected);
        result.ErrorCode.Should().Be("PaymentAmountMismatch");
    }

    // A payment already in a terminal state, or one whose confirmed amount disagrees with the
    // record, will refuse the same event forever. Providers read a non-2xx as "retry" — Yandex Pay
    // for 24h — so these have to be separable from the refusals a redelivery could still fix.

    [Theory]
    [InlineData(ErrorCodes.PaymentInvalidTransition)]
    [InlineData(ErrorCodes.PaymentAmountMismatch)]
    [InlineData(ErrorCodes.PaymentAlreadyProcessed)]
    public async Task apply_rejected_for_a_final_reason_is_RejectedPermanent(string errorCode)
    {
        var billing = new FakeBillingProvider
        {
            NextApplyResult = new ApplyPaymentEventResult(false, null, ErrorCode: errorCode)
        };

        var result = await CreateProcessor(billing).ProcessAsync(
            "yandexpay", CanceledEvent(), rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.RejectedPermanent);
        result.ErrorCode.Should().Be(errorCode);
    }

    [Theory]
    [InlineData(ErrorCodes.FxQuoteUnavailable)]
    [InlineData(ErrorCodes.PaymentProviderUnavailable)]
    [InlineData(ErrorCodes.InternalError)]
    [InlineData(ErrorCodes.PaymentNotFound)]
    public async Task apply_rejected_for_a_reason_that_may_clear_stays_retryable(string errorCode)
    {
        var billing = new FakeBillingProvider
        {
            NextApplyResult = new ApplyPaymentEventResult(false, null, ErrorCode: errorCode)
        };

        var result = await CreateProcessor(billing).ProcessAsync(
            "yandexpay", CanceledEvent(), rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.Rejected);
    }

    [Fact]
    public async Task confirm_rejected_for_a_final_reason_is_RejectedPermanent()
    {
        var billing = new FakeBillingProvider
        {
            NextConfirmResult = new ConfirmPaymentResult(
                false, null, null, ErrorCode: ErrorCodes.PaymentInvalidTransition)
        };
        var evt = new PaymentWebhookEvent(
            ProviderPaymentId: "yp-1", OrderId: "pay-1",
            EventType: "paid",
            ConfirmedAmount: 81.13m, ConfirmedCurrency: "RUB",
            ProviderEventId: null, Reason: null);

        var result = await CreateProcessor(billing).ProcessAsync(
            "yandexpay", evt, rawPayload: "{}", signatureOk: true, CancellationToken.None);

        result.Outcome.Should().Be(PaymentWebhookProcessor.ProcessOutcome.RejectedPermanent);
    }

    private static PaymentWebhookEvent CanceledEvent() => new(
        ProviderPaymentId: "yp-1",
        OrderId: "pay-1",
        EventType: "canceled",
        ConfirmedAmount: null,
        ConfirmedCurrency: null,
        ProviderEventId: null,
        Reason: null);
}
