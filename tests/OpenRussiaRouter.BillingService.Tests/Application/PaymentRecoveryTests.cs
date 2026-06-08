using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class PaymentRecoveryTests
{
    // ── Lease tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcquireStalePayments_SecondWorkerWithinLease_GetsEmpty()
    {
        var repo = new InMemoryBillingRepository();
        var staleTime = DateTimeOffset.UtcNow.AddHours(-1);
        await repo.CreatePaymentAsync(StalePayment("p-1", staleTime));

        var first = await repo.AcquireStalePaymentsAsync("worker-1", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, 10);
        var second = await repo.AcquireStalePaymentsAsync("worker-2", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, 10);

        first.Should().ContainSingle(p => p.Id == "p-1");
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task AcquireStalePayments_AfterLeaseExpires_PaymentAvailableAgain()
    {
        var repo = new InMemoryBillingRepository();
        var staleTime = DateTimeOffset.UtcNow.AddHours(-1);
        await repo.CreatePaymentAsync(StalePayment("p-lease", staleTime));

        await repo.AcquireStalePaymentsAsync("worker-1", TimeSpan.FromMilliseconds(80), DateTimeOffset.UtcNow, 10);
        var beforeExpiry = await repo.AcquireStalePaymentsAsync("worker-2", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, 10);

        await Task.Delay(150);

        var afterExpiry = await repo.AcquireStalePaymentsAsync("worker-2", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, 10);

        beforeExpiry.Should().BeEmpty();
        afterExpiry.Should().ContainSingle(p => p.Id == "p-lease");
    }

    [Fact]
    public async Task AcquireStalePayments_SuccessfulConfirm_ClearsLease()
    {
        var (service, repo) = CreateService();
        var staleTime = DateTimeOffset.UtcNow.AddHours(-1);
        await repo.CreatePaymentAsync(StalePayment("p-clear", staleTime));

        var acquired = await repo.AcquireStalePaymentsAsync("worker-1", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, 10);
        acquired.Should().ContainSingle();

        // Confirm the payment (simulates successful reconciliation)
        var payment = acquired.First();
        await service.ConfirmPaymentByProviderAsync(new PaymentEventInput(
            payment.Provider, payment.ProviderPaymentId, payment.OrderId, "paid",
            payment.ProviderAmount, payment.ProviderCurrency, null, "evt-1", "{}", true),
            DateTimeOffset.UtcNow);

        // After confirm, lease fields should be cleared
        var updated = await repo.GetPaymentAsync("p-clear");
        updated!.LeaseOwner.Should().BeNull();
        updated.LeaseUntilUtc.Should().BeNull();
    }

    // ── Recovery tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Recovery_UnprocessedPaidEvent_CreditedOnce()
    {
        var (service, repo) = CreateService();
        var payment = await service.CreatePaymentAsync("user-1", "mock", 10, "USD", 10, "USD", "tok-1", DateTimeOffset.UtcNow);

        // Simulate crash after tx1 (event inserted but tx2 not executed)
        repo.InjectPaymentEvent(new PaymentEvent(
            "evt-crash", payment.Id, "mock", "provider-evt-1", null, "paid",
            payment.OrderId, 10m, "USD", null, "{}", true, false, 0, DateTimeOffset.UtcNow, null));

        var processed = await repo.AcquireUnprocessedEventsAsync(10, DateTimeOffset.UtcNow);
        processed.Should().Be(1);

        // Balance must be credited exactly once
        (await repo.GetBalanceAsync("user-1")).Amount.Should().Be(10);

        // Event must be marked processed
        (await repo.ListUnprocessedEventsAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Recovery_DoubleProcessing_CreditedOnce()
    {
        var (service, repo) = CreateService();
        var payment = await service.CreatePaymentAsync("user-1", "mock", 10, "USD", 10, "USD", "tok-2", DateTimeOffset.UtcNow);

        repo.InjectPaymentEvent(new PaymentEvent(
            "evt-dup", payment.Id, "mock", "provider-evt-2", null, "paid",
            payment.OrderId, 10m, "USD", null, "{}", true, false, 0, DateTimeOffset.UtcNow, null));

        // Simulate two replicas concurrently acquiring and processing the same event.
        var results = await Task.WhenAll(
            repo.AcquireUnprocessedEventsAsync(10, DateTimeOffset.UtcNow),
            repo.AcquireUnprocessedEventsAsync(10, DateTimeOffset.UtcNow));

        results.Sum().Should().Be(1);

        (await repo.GetBalanceAsync("user-1")).Amount.Should().Be(10);
        (await repo.ListUnprocessedEventsAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Recovery_CancelEvent_NotUnprocessedAfterApply()
    {
        var (service, repo) = CreateService();
        var payment = await service.CreatePaymentAsync("user-2", "mock", 5, "USD", 5, "USD", "tok-3", DateTimeOffset.UtcNow);

        repo.InjectPaymentEvent(new PaymentEvent(
            "evt-cancel", payment.Id, "mock", "provider-evt-cancel", null, "canceled",
            payment.OrderId, null, null, "user canceled", "{}", true, false, 0, DateTimeOffset.UtcNow, null));

        var processed = await repo.AcquireUnprocessedEventsAsync(10, DateTimeOffset.UtcNow);

        processed.Should().Be(1);
        (await repo.GetPaymentAsync(payment.Id))!.Status.Should().Be(PaymentStatus.Canceled);
        (await repo.ListUnprocessedEventsAsync(10)).Should().BeEmpty();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Payment StalePayment(string id, DateTimeOffset createdAt) =>
        new(id, "user-1", "mock", null, id, "client-" + id,
            10, "USD", 10, "USD", MoneyAmount.Usd(10), 1m, "fallback", createdAt,
            PaymentStatus.Created, null, null, null, createdAt, createdAt, null, null, null);

    private static (BillingApplicationService Service, InMemoryBillingRepository Repository) CreateService()
    {
        var repo = new InMemoryBillingRepository();
        var fx = new FxQuoteService(new FakeHttpClientFactory(),
            Options.Create(new ExchangeRateOptions
            {
                Fallback = new ExchangeRateFallbackOptions { RubPerUsd = 100, RubPerEur = 110 }
            }), NullLogger<FxQuoteService>.Instance);
        return (new BillingApplicationService(repo, new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 100, MaxPageSize = 1000 }),
            NullLogger<BillingApplicationService>.Instance, fx,
            Options.Create(new PaymentOptions { MinTopUpUsd = 1, MaxTopUpUsd = 10000 })), repo);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
