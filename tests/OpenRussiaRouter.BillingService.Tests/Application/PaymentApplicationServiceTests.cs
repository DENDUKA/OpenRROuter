using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
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

    [Fact]
    public async Task GrantFirstTopUpBonus_OnFirstConfirmedPayment_GrantsBonus()
    {
        // T-001 regression: confirmation inserts the ExternalTopUp ledger entry with is_user_payment=true
        // BEFORE GrantFirstTopUpBonusAsync runs, so the "first paid top-up" check must exclude the
        // current payment (idempotency_key = 'payment:{paymentId}') — otherwise the bonus is never granted.
        var (service, _) = CreateService();
        var now = DateTimeOffset.UtcNow;
        var first = await CreatePaymentWithBonus(service, "first-bonus-token", 20m, now);

        var outcome = await service.ConfirmPaymentByProviderAsync(Paid(first, "first-paid-event"), now);
        outcome.Confirmed.Should().BeTrue();

        var grant = await service.GrantFirstTopUpBonusAsync(first.Id, now);

        grant.Granted.Should().BeTrue();
        grant.ErrorCode.Should().BeNull();
        grant.LedgerEntryId.Should().NotBeNull();
        // 10 (credited) + 2 (20% bonus) = 12
        (await service.GetBalanceAsync("user-1")).Amount.Should().Be(12m);
    }

    [Fact]
    public async Task GrantFirstTopUpBonus_OnSecondConfirmedPayment_DoesNotGrant()
    {
        var (service, _) = CreateService();
        var now = DateTimeOffset.UtcNow;
        var first = await CreatePaymentWithBonus(service, "first-bonus-token", 20m, now);
        await service.ConfirmPaymentByProviderAsync(Paid(first, "first-paid-event"), now);
        (await service.GrantFirstTopUpBonusAsync(first.Id, now)).Granted.Should().BeTrue();

        // Second payment for the same user — bonus must NOT be granted.
        var second = await CreatePaymentWithBonus(service, "second-bonus-token", 20m, now);
        await service.ConfirmPaymentByProviderAsync(Paid(second, "second-paid-event"), now);

        var grant = await service.GrantFirstTopUpBonusAsync(second.Id, now);

        grant.Granted.Should().BeFalse();
        grant.ErrorCode.Should().Be("not_first_paid_top_up");
        grant.LedgerEntryId.Should().BeNull();
        // 10 (first) + 2 (first bonus) + 10 (second, no bonus) = 22
        (await service.GetBalanceAsync("user-1")).Amount.Should().Be(22m);
    }

    [Fact]
    public async Task GrantFirstTopUpBonus_IsIdempotent_PerPaymentId()
    {
        var (service, _) = CreateService();
        var now = DateTimeOffset.UtcNow;
        var payment = await CreatePaymentWithBonus(service, "idempotent-token", 20m, now);
        await service.ConfirmPaymentByProviderAsync(Paid(payment, "paid-event"), now);

        var first = await service.GrantFirstTopUpBonusAsync(payment.Id, now);
        var second = await service.GrantFirstTopUpBonusAsync(payment.Id, now);

        first.Granted.Should().BeTrue();
        // Re-delivery of the same payment must be a no-op (Granted=false, no error code, no second entry).
        second.Granted.Should().BeFalse();
        second.ErrorCode.Should().BeNull();
        second.LedgerEntryId.Should().BeNull();
        // Only one bonus entry credited.
        (await service.GetBalanceAsync("user-1")).Amount.Should().Be(12m);
    }

    [Fact]
    public async Task GrantFirstTopUpBonus_RoundsBonusToSixPlacesWithBankersRounding()
    {
        // T-033 regression: bonus_usd must be rounded to 6 decimal places in C# with the
        // project-wide bankers'-rounding policy so that ledger_entries.amount and
        // payments.bonus_usd stay byte-for-byte equal (numeric(18,6) vs numeric(12,6)).
        // 123.445 RUB ÷ 100 (fallback RubPerUsd) = 1.23445 USD credited;
        // raw bonus = 1.23445 * 1 / 100 = 0.0123445 (7 fractional digits); the 7th digit is 5
        // (exactly half) and the 6th is 4 (even) → bankers' rounding goes DOWN to 0.012344
        // (away-from-zero would round up to 0.012345).
        var (service, repository) = CreateService();
        var now = DateTimeOffset.UtcNow;
        const decimal providerAmount = 123.445m;
        const decimal bonusPercent = 1m;
        const decimal expectedCreditedUsd = 1.23445m;
        const decimal expectedBonusUsd = 0.012344m;

        var payment = await service.CreatePaymentAsync("user-1", "mock", providerAmount, "RUB",
            providerAmount, "RUB", "round-token", now, bonusPercent: bonusPercent);

        payment.CreditedUsd.Amount.Should().Be(expectedCreditedUsd);
        (await service.ConfirmPaymentByProviderAsync(Paid(payment, "paid-round"), now)).Confirmed.Should().BeTrue();

        var grant = await service.GrantFirstTopUpBonusAsync(payment.Id, now);
        grant.Granted.Should().BeTrue();
        grant.ErrorCode.Should().BeNull();

        var ledger = await repository.GetFirstTopUpBonusForPaymentAsync(payment.Id);
        ledger!.Value.Amount.Should().Be(expectedBonusUsd);

        var stored = await service.GetPaymentAsync(payment.Id);
        stored!.BonusUsd.Should().Be(expectedBonusUsd);
        stored!.BonusUsd.Should().Be(ledger!.Value.Amount);

        (await service.GetBalanceAsync("user-1")).Amount
            .Should().Be(expectedCreditedUsd + expectedBonusUsd);
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

    private static Task<Payment> CreatePaymentWithBonus(
        BillingApplicationService service, string token, decimal bonusPercent, DateTimeOffset nowUtc) =>
        service.CreatePaymentAsync("user-1", "mock", 10, "USD", 10, "USD", token, nowUtc, bonusPercent: bonusPercent);

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
