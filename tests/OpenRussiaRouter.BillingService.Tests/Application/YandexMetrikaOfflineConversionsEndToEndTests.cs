using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BillingService.Infrastructure.Workers;
using OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Tests.Application;

/// <summary>
/// End-to-end integration test verifying the full pipeline:
/// Consent + Cookie -> Identity Store -> Payment Confirmation & Usage -> Outbox Enqueue -> Worker Lease & Upload.
/// </summary>
public sealed class YandexMetrikaOfflineConversionsEndToEndTests
{
    [Fact]
    public async Task FullFlow_IdentityCapture_TopUpAndFirstUsage_WorkerUploadsSuccessfully()
    {
        var now = new DateTimeOffset(2026, 9, 2, 14, 0, 0, TimeSpan.Zero);
        var userId = "user-e2e-1";
        var clientId = "987654321098";

        // 1. Setup InMemory Identity Store (simulating WebApp saving user_id -> client_id)
        var identityStore = new InMemoryYandexMetrikaIdentityStore();
        await identityStore.UpsertAsync(userId, clientId, now, CancellationToken.None);

        var fetchedClientId = await identityStore.GetClientIdAsync(userId, CancellationToken.None);
        fetchedClientId.Should().Be(clientId);

        // 2. Setup InMemory Billing Repository
        var repository = new InMemoryBillingRepository();

        // 3. User creates a payment and confirms it
        var payment = new Payment(
            "payment-e2e-1",
            userId,
            "rollypay",
            null,
            "order-e2e-1",
            "client-token-1",
            1500m,
            "RUB",
            1500m,
            "RUB",
            MoneyAmount.Usd(15m),
            100m,
            "cbr",
            now,
            PaymentStatus.Created,
            null,
            null,
            null,
            now,
            now,
            null,
            null,
            null);
        await repository.CreatePaymentAsync(payment);

        var paymentEvent = new PaymentEventInput(
            "rollypay",
            "provider-payment-1",
            "order-e2e-1",
            "paid",
            1500m,
            "RUB",
            null,
            "provider-event-1",
            "{}",
            true);
        var confirmOutcome = await repository.ConfirmPaymentAtomicAsync(paymentEvent, now);
        confirmOutcome.Confirmed.Should().BeTrue();

        // 4. User makes first API request
        await repository.UpsertModelAsync(new ModelPrice(
            "gpt-4o",
            "GPT-4o",
            "gpt-4o-router",
            "v1",
            new ModelTokenPrice(MoneyAmount.Usd(5m), MoneyAmount.Usd(15m), MoneyAmount.Usd(2.5m)),
            Enabled: true));

        var activity = new ActivityByTokenRecord(
            "req-e2e-1",
            "key-e2e-1",
            "sk-or-test",
            "gpt-4o",
            "node-1",
            new TokenUsage(100, 50, 0),
            MoneyAmount.Usd(0.00125m),
            "completed",
            now,
            now);

        var usage = new UsageRecord(
            "req-e2e-1",
            userId,
            "key-e2e-1",
            "gpt-4o",
            "v1",
            "node-1",
            new TokenUsage(100, 50, 0),
            MoneyAmount.Usd(0.00125m),
            "succeeded",
            now);

        var ledgerEntry = new LedgerEntry(
            "ledger-e2e-1",
            userId,
            LedgerEntryType.UsageCharge,
            MoneyAmount.Usd(-0.00125m),
            "usage:req-e2e-1",
            "Usage charge",
            now);

        await repository.RecordUsageWithTransactionAsync(activity, usage, ledgerEntry);

        // 5. Check outbox has both conversions enqueued
        var acquired = await repository.AcquireYandexMetrikaOfflineConversionsAsync(
            "worker-peek",
            TimeSpan.FromSeconds(1),
            now.AddSeconds(1),
            10,
            CancellationToken.None);

        acquired.Should().HaveCount(2);
        var topupConversion = acquired.First(c => c.Target == "topup_completed");
        var usageConversion = acquired.First(c => c.Target == "first_api_request");

        topupConversion.DedupKey.Should().Be("topup_completed:payment-e2e-1");
        topupConversion.Price.Should().Be(1500m);
        topupConversion.Currency.Should().Be("RUB");
        topupConversion.Status.Should().Be(YandexMetrikaOfflineConversionStatus.Pending);

        usageConversion.DedupKey.Should().Be($"first_api_request:{userId}");
        usageConversion.Price.Should().BeNull();
        usageConversion.Currency.Should().BeNull();
        usageConversion.Status.Should().Be(YandexMetrikaOfflineConversionStatus.Pending);

        // 6. Setup Mock Yandex Metrika Client
        var uploadedItems = new List<(YandexMetrikaOfflineConversion Conversion, string ClientId)>();
        var mockMetrikaClient = new TestMetrikaClient((conv, cid) =>
        {
            uploadedItems.Add((conv, cid));
            return Task.FromResult(new YandexMetrikaUploading(
                uploadedItems.Count,
                1,
                1,
                conv.Comment,
                "PROCESSED"));
        });

        var options = Options.Create(new YandexMetrikaOfflineConversionOptions
        {
            Enabled = true,
            CounterId = 111582266,
            OAuthToken = "test-oauth-token",
            BaseUrl = "https://api-metrika.yandex.net/",
            WorkerInterval = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(2),
            MissingIdentityRetryInterval = TimeSpan.FromHours(1),
            UploadStatusPollInterval = TimeSpan.FromMinutes(5),
            BaseRetryDelay = TimeSpan.FromMinutes(1),
            MaxRetryDelay = TimeSpan.FromHours(1),
            ConfigurationRetryInterval = TimeSpan.FromMinutes(15),
            ExpirationWindow = TimeSpan.FromDays(20),
            HttpTimeout = TimeSpan.FromSeconds(30),
            BatchSize = 20
        });

        var worker = new YandexMetrikaOfflineConversionWorker(
            repository,
            identityStore,
            mockMetrikaClient,
            options,
            new FixedTimeProvider(now.AddSeconds(5)),
            NullLogger<YandexMetrikaOfflineConversionWorker>.Instance);

        // 7. Execute worker batch
        await worker.ProcessBatchAsync(CancellationToken.None);

        // Verify uploads
        uploadedItems.Should().HaveCount(2);
        uploadedItems.Should().OnlyContain(x => x.ClientId == clientId);

        // Verify CSV construction for both
        var topupCsv = YandexMetrikaClient.BuildCsv(topupConversion, clientId);
        topupCsv.Should().Contain("ClientId,Target,DateTime,Price,Currency");
        topupCsv.Should().Contain($"{clientId},topup_completed,{now.ToUnixTimeSeconds()},1500,RUB");

        var usageCsv = YandexMetrikaClient.BuildCsv(usageConversion, clientId);
        usageCsv.Should().Contain("ClientId,Target,DateTime,Price,Currency");
        usageCsv.Should().Contain($"{clientId},first_api_request,{now.ToUnixTimeSeconds()},,");

        // 8. Subsequent acquire should return 0 since they are now Processed
        var remaining = await repository.AcquireYandexMetrikaOfflineConversionsAsync(
            "worker-1",
            TimeSpan.FromMinutes(2),
            now.AddSeconds(10),
            10,
            CancellationToken.None);
        remaining.Should().BeEmpty();
    }

    private sealed class InMemoryYandexMetrikaIdentityStore : IYandexMetrikaIdentityStore
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

        public Task UpsertAsync(string userId, string clientId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
        {
            _store[userId] = clientId;
            return Task.CompletedTask;
        }

        public Task<string?> GetClientIdAsync(string userId, CancellationToken cancellationToken)
        {
            _store.TryGetValue(userId, out var clientId);
            return Task.FromResult(clientId);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class TestMetrikaClient(
        Func<YandexMetrikaOfflineConversion, string, Task<YandexMetrikaUploading>> uploadFunc) : IYandexMetrikaClient
    {
        public Task<YandexMetrikaUploading> UploadAsync(
            YandexMetrikaOfflineConversion conversion,
            string clientId,
            CancellationToken ct) => uploadFunc(conversion, clientId);

        public Task<YandexMetrikaUploading?> FindByCommentAsync(string comment, CancellationToken ct) =>
            Task.FromResult<YandexMetrikaUploading?>(null);

        public Task<YandexMetrikaUploading> GetUploadingAsync(long uploadId, CancellationToken ct) =>
            Task.FromResult(new YandexMetrikaUploading(uploadId, 1, 1, "test", "PROCESSED"));
    }
}
