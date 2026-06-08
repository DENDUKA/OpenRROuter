using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Usage;
using System.Globalization;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class PaymentApplicationServiceTests
{
    [Fact]
    public async Task CreateConfirmAndRepeatConfirm_CreditsTrustedAmountOnce()
    {
        var (service, _) = CreateService();
        var payment = await CreatePayment(service, "token-1");

        payment.CreditedUsd.Amount.Should().Be(10);
        var first = await service.ConfirmPaymentByProviderAsync(Paid(payment, "event-1"), DateTimeOffset.UtcNow);
        var second = await service.ConfirmPaymentByProviderAsync(Paid(payment, "event-2"), DateTimeOffset.UtcNow);

        first.Confirmed.Should().BeTrue();
        second.Confirmed.Should().BeFalse();
        second.ErrorCode.Should().BeNull();
        (await service.GetBalanceAsync("user-1")).Amount.Should().Be(10);
        (await service.ListTopUpsAsync("user-1")).Should().ContainSingle(e => e.Type == LedgerEntryType.ExternalTopUp);
    }

    [Fact]
    public async Task CreatePayment_SameClientToken_ReturnsExistingPayment()
    {
        var (service, _) = CreateService();
        var first = await CreatePayment(service, "same");
        var second = await CreatePayment(service, "same");

        second.Id.Should().Be(first.Id);
    }

    [Theory]
    [InlineData("0.50")]
    [InlineData("10001")]
    public async Task CreatePayment_OutsideLimits_IsRejected(string amount)
    {
        var (service, _) = CreateService();

        var parsed = decimal.Parse(amount, CultureInfo.InvariantCulture);
        var act = () => service.CreatePaymentAsync("user-1", "mock", parsed, "USD",
            parsed, "USD", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

        (await act.Should().ThrowAsync<PaymentOperationException>()).Which.ErrorCode
            .Should().Be(ErrorCodes.PaymentAmountOutOfRange);
    }

    [Fact]
    public async Task Confirm_MismatchedAmount_IsRejectedAndCreatesAnomaly()
    {
        var (service, _) = CreateService();
        var payment = await CreatePayment(service, "token-mismatch");

        var outcome = await service.ConfirmPaymentByProviderAsync(Paid(payment, "mismatch") with { ConfirmedAmount = 9m }, DateTimeOffset.UtcNow);

        outcome.ErrorCode.Should().Be(ErrorCodes.PaymentAmountMismatch);
        (await service.GetBalanceAsync("user-1")).Amount.Should().Be(0);
        (await service.ListAnomaliesAsync()).Items.Should().ContainSingle(a => a.Type == "payment_amount_mismatch");
    }

    [Fact]
    public async Task CancelFromCreated_Applies_ButCancelFromPaidFails()
    {
        var (service, _) = CreateService();
        var created = await CreatePayment(service, "cancel-created");
        var canceled = await service.ApplyPaymentEventAsync(Event(created, "canceled", "cancel-1"), DateTimeOffset.UtcNow);
        var paid = await CreatePayment(service, "cancel-paid");
        await service.ConfirmPaymentByProviderAsync(Paid(paid, "paid-1"), DateTimeOffset.UtcNow);
        var invalid = await service.ApplyPaymentEventAsync(Event(paid, "canceled", "cancel-2"), DateTimeOffset.UtcNow);

        canceled.Applied.Should().BeTrue();
        canceled.Payment!.Status.Should().Be(PaymentStatus.Canceled);
        invalid.ErrorCode.Should().Be(ErrorCodes.PaymentInvalidTransition);
    }

    [Fact]
    public async Task RefundReversesButChargebackDefaultDoesNot()
    {
        var (service, _) = CreateService();
        var refund = await CreatePayment(service, "refund");
        await service.ConfirmPaymentByProviderAsync(Paid(refund, "paid-refund"), DateTimeOffset.UtcNow);
        var refunded = await service.ApplyPaymentEventAsync(Event(refund, "refunded", "refund-event"), DateTimeOffset.UtcNow);
        var chargeback = await CreatePayment(service, "chargeback");
        await service.ConfirmPaymentByProviderAsync(Paid(chargeback, "paid-chargeback"), DateTimeOffset.UtcNow);
        var chargedBack = await service.ApplyPaymentEventAsync(Event(chargeback, "chargeback", "chargeback-event"), DateTimeOffset.UtcNow);

        refunded.LedgerEntryId.Should().NotBeNull();
        chargedBack.LedgerEntryId.Should().BeNull();
        (await service.GetBalanceAsync("user-1")).Amount.Should().Be(10);
    }

    private static (BillingApplicationService Service, InMemoryBillingRepository Repository) CreateService()
    {
        var repository = new InMemoryBillingRepository();
        var fx = new FxQuoteService(new FakeHttpClientFactory(),
            Options.Create(new ExchangeRateOptions
            {
                Fallback = new ExchangeRateFallbackOptions { RubPerUsd = 100, RubPerEur = 110 }
            }), NullLogger<FxQuoteService>.Instance);
        return (new BillingApplicationService(repository, new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 100, MaxPageSize = 1000 }),
            NullLogger<BillingApplicationService>.Instance, fx,
            Options.Create(new PaymentOptions { MinTopUpUsd = 1, MaxTopUpUsd = 10000 })), repository);
    }

    private static Task<Payment> CreatePayment(BillingApplicationService service, string token) =>
        service.CreatePaymentAsync("user-1", "mock", 10, "USD", 10, "USD", token, DateTimeOffset.UtcNow);

    private static PaymentEventInput Paid(Payment payment, string eventId) =>
        new(payment.Provider, payment.ProviderPaymentId, payment.OrderId, "paid", payment.ProviderAmount,
            payment.ProviderCurrency, null, eventId, "{}", true);

    private static PaymentEventInput Event(Payment payment, string type, string eventId) =>
        new(payment.Provider, payment.ProviderPaymentId, payment.OrderId, type, null, null, type, eventId, "{}", true);

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
