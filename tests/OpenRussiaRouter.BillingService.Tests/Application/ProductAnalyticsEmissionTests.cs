using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;
using System.Net.Http;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class ProductAnalyticsEmissionTests
{
    [Fact]
    public async Task RecordUsage_Success_EmitsApiRequestCompletedWithBuckets()
    {
        var (service, analytics) = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usageEvent = CreateUsageEvent("req-1", new TokenUsage(100, 200, 0));

        var result = await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        await WaitUntil(() => analytics.ApiRequestCompletedCalls.Count == 1);
        var call = analytics.ApiRequestCompletedCalls.Single();
        call.UserId.Should().Be("user-1");
        call.Data.Model.Should().Be("router/gpt-test");
        call.Data.InputTokens.Should().Be(100);
        call.Data.OutputTokens.Should().Be(200);

        var built = BuildApiRequestEvent(call.UserId, call.Data);
        built.Props["input_tokens_bucket"].Should().Be("1-1K");
        built.Props["output_tokens_bucket"].Should().Be("1-1K");
        built.Props["latency_bucket"].Should().Be("0-100ms");
        built.Props["status_class"].Should().Be("2xx");
        // No endpoint/stream on the usage event → safe default: the properties are omitted
        // rather than emitted with placeholder values (see TS-016 §4).
        built.Props.Should().NotContainKey("endpoint");
        built.Props.Should().NotContainKey("stream");
        built.Props.Should().NotContainKey("input_tokens");
        built.Props.Should().NotContainKey("output_tokens");
    }

    [Fact]
    public async Task RecordUsage_StreamingChat_EmitsChatCompletionsEndpointAndStreamTrue()
    {
        var (service, analytics) = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usageEvent = CreateUsageEvent("req-stream", new TokenUsage(100, 200, 0), endpoint: "chat_completions", stream: true);

        await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);

        await WaitUntil(() => analytics.ApiRequestCompletedCalls.Count == 1);
        var call = analytics.ApiRequestCompletedCalls.Single();
        call.Data.Endpoint.Should().Be(ApiEndpoint.ChatCompletions);
        call.Data.Stream.Should().BeTrue();

        var built = BuildApiRequestEvent(call.UserId, call.Data);
        built.Props["endpoint"].Should().Be("chat_completions");
        built.Props["stream"].Should().Be(true);
    }

    [Fact]
    public async Task RecordUsage_Tts_EmitsTtsEndpointAndStreamFalse()
    {
        var (service, analytics) = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usageEvent = CreateUsageEvent("req-tts", new TokenUsage(0, 50, 0), endpoint: "tts", stream: false);

        await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);

        await WaitUntil(() => analytics.ApiRequestCompletedCalls.Count == 1);
        var call = analytics.ApiRequestCompletedCalls.Single();
        call.Data.Endpoint.Should().Be(ApiEndpoint.Tts);
        call.Data.Stream.Should().BeFalse();

        var built = BuildApiRequestEvent(call.UserId, call.Data);
        built.Props["endpoint"].Should().Be("tts");
        built.Props["stream"].Should().Be(false);
    }

    [Fact]
    public async Task RecordUsage_UnknownEndpoint_OmitsEndpointWithoutThrowing()
    {
        var (service, analytics) = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usageEvent = CreateUsageEvent("req-unknown", new TokenUsage(1, 1, 0), endpoint: "bogus", stream: true);

        var result = await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        await WaitUntil(() => analytics.ApiRequestCompletedCalls.Count == 1);
        var call = analytics.ApiRequestCompletedCalls.Single();
        call.Data.Endpoint.Should().BeNull();

        var built = BuildApiRequestEvent(call.UserId, call.Data);
        built.Props.Should().NotContainKey("endpoint");
        built.Props["stream"].Should().Be(true);
    }

    [Fact]
    public async Task RecordUsage_IdempotentRepeat_EmitsWithSameDedupKey()
    {
        var (service, analytics) = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usageEvent = CreateUsageEvent("req-idempotent", new TokenUsage(1, 1, 0));

        await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);
        await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);

        await WaitUntil(() => analytics.ApiRequestCompletedCalls.Count == 2);
        analytics.ApiRequestCompletedCalls[0].Data.DedupKey.Should().Be("req-idempotent");
        analytics.ApiRequestCompletedCalls[1].Data.DedupKey.Should().Be("req-idempotent");
    }

    [Fact]
    public async Task ConfirmPayment_FirstConfirm_EmitsPaymentCompleted()
    {
        var (service, analytics) = CreateService();
        var payment = await CreatePayment(service, "token-1");

        var first = await service.ConfirmPaymentByProviderAsync(Paid(payment, "event-1"), DateTimeOffset.UtcNow);

        first.Confirmed.Should().BeTrue();
        await WaitUntil(() => analytics.PaymentCompletedCalls.Count == 1);
        var call = analytics.PaymentCompletedCalls.Single();
        call.UserId.Should().Be("user-1");
        call.Data.Provider.Should().Be("mock");
        call.Data.Currency.Should().Be("USD");
        call.Data.Amount.Should().Be(10m);
        call.Data.DedupKey.Should().Be(payment.Id);
    }

    [Fact]
    public async Task ConfirmPayment_RepeatConfirm_DoesNotEmitSecondPaymentCompleted()
    {
        var (service, analytics) = CreateService();
        var payment = await CreatePayment(service, "token-1");

        await service.ConfirmPaymentByProviderAsync(Paid(payment, "event-1"), DateTimeOffset.UtcNow);
        var second = await service.ConfirmPaymentByProviderAsync(Paid(payment, "event-2"), DateTimeOffset.UtcNow);

        second.Confirmed.Should().BeFalse();
        await WaitUntil(() => analytics.PaymentCompletedCalls.Count == 1);
        analytics.PaymentCompletedCalls.Single().Data.DedupKey.Should().Be(payment.Id);
    }

    [Fact]
    public async Task AnalyticsThrows_RecordUsageStillSucceeds()
    {
        var service = CreateServiceWithThrowingAnalytics();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usageEvent = CreateUsageEvent("req-fail", new TokenUsage(1, 1, 0));

        var result = await service.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyticsThrows_ConfirmPaymentStillSucceeds()
    {
        var service = CreateServiceWithThrowingAnalytics();
        var payment = await CreatePayment(service, "token-fail");

        var result = await service.ConfirmPaymentByProviderAsync(Paid(payment, "event-fail"), DateTimeOffset.UtcNow);

        result.Confirmed.Should().BeTrue();
    }

    private static (BillingApplicationService Service, RecordingProductAnalytics Analytics) CreateService()
    {
        var repository = new InMemoryBillingRepository();
        var analytics = new RecordingProductAnalytics();
        var service = new BillingApplicationService(
            repository,
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance,
            CreateFx(),
            null,
            analytics);
        return (service, analytics);
    }

    private static BillingApplicationService CreateServiceWithThrowingAnalytics()
    {
        return new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance,
            CreateFx(),
            null,
            new ThrowingProductAnalytics());
    }

    private static FxQuoteService CreateFx() =>
        new(new FakeHttpClientFactory(),
            Options.Create(new ExchangeRateOptions
            {
                Fallback = new ExchangeRateFallbackOptions { RubPerUsd = 100, RubPerEur = 110 }
            }), NullLogger<FxQuoteService>.Instance);

    private static ModelPrice CreateModel(string modelId, bool enabled)
    {
        return new ModelPrice(
            modelId,
            modelId,
            $"router/{modelId}",
            "price-v1",
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(1)),
            enabled);
    }

    private static UsageEvent CreateUsageEvent(
        string requestId,
        TokenUsage usage,
        string status = "succeeded",
        string? endpoint = null,
        bool? stream = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new UsageEvent(
            requestId,
            "user-1",
            "key-1",
            "sk-or-test",
            "router/gpt-test",
            "router-1",
            usage,
            status,
            now.AddMilliseconds(-10),
            now,
            Endpoint: endpoint,
            Stream: stream);
    }

    private static Task<Payment> CreatePayment(BillingApplicationService service, string token) =>
        service.CreatePaymentAsync("user-1", "mock", 10, "USD", 10, "USD", token, DateTimeOffset.UtcNow);

    private static PaymentEventInput Paid(Payment payment, string eventId) =>
        new(payment.Provider, payment.ProviderPaymentId, payment.OrderId, "paid", payment.ProviderAmount,
            payment.ProviderCurrency, null, eventId, "{}", true);

    private static AnalyticsEvent BuildApiRequestEvent(string userId, ApiRequestAnalytics data)
    {
        var builder = new AnalyticsEventBuilder(
            new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = "test-salt" }),
            "production");
        return builder.BuildApiRequestCompleted(userId, data);
    }

    private static async Task WaitUntil(Func<bool> predicate, int timeoutMs = 2000)
    {
        var started = DateTimeOffset.UtcNow;
        while (!predicate())
        {
            if ((DateTimeOffset.UtcNow - started).TotalMilliseconds > timeoutMs)
                throw new TimeoutException("Predicate was not satisfied in time.");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingProductAnalytics : IProductAnalytics
    {
        private readonly object _lock = new();
        private readonly List<(string UserId, ApiRequestAnalytics Data)> _apiRequestCompleted = [];
        private readonly List<(string UserId, PaymentAnalytics Data)> _paymentCompleted = [];

        public IReadOnlyList<(string UserId, ApiRequestAnalytics Data)> ApiRequestCompletedCalls
        {
            get { lock (_lock) { return _apiRequestCompleted.ToArray(); } }
        }

        public IReadOnlyList<(string UserId, PaymentAnalytics Data)> PaymentCompletedCalls
        {
            get { lock (_lock) { return _paymentCompleted.ToArray(); } }
        }

        public void UserVerified(string userId, string signupMethod)
        {
        }

        public void ApiKeyCreated(string userId)
        {
        }

        public void ApiRequestCompleted(string userId, ApiRequestAnalytics data)
        {
            lock (_lock) { _apiRequestCompleted.Add((userId, data)); }
        }

        public void PaymentCompleted(string userId, PaymentAnalytics data)
        {
            lock (_lock) { _paymentCompleted.Add((userId, data)); }
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class ThrowingProductAnalytics : IProductAnalytics
    {
        public void UserVerified(string userId, string signupMethod) => throw new InvalidOperationException("analytics failure");

        public void ApiKeyCreated(string userId) => throw new InvalidOperationException("analytics failure");

        public void ApiRequestCompleted(string userId, ApiRequestAnalytics data) => throw new InvalidOperationException("analytics failure");

        public void PaymentCompleted(string userId, PaymentAnalytics data) => throw new InvalidOperationException("analytics failure");
    }
}
