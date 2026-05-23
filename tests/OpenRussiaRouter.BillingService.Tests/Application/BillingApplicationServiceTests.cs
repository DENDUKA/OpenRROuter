using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class BillingApplicationServiceTests
{
    [Fact]
    public async Task CreateVirtualTopUp_PositiveRub_IncreasesBalance()
    {
        var service = CreateService();

        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Rub(100));
    }

    [Fact]
    public async Task CreateVirtualTopUp_SameIdempotencyKey_DoesNotIncreaseBalanceTwice()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;

        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", now);
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", now);

        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Rub(100));
    }

    [Fact]
    public async Task CheckBalanceAndModel_PositiveBalanceAndEnabledModel_ReturnsAllowed()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.CheckBalanceAndModelAsync("user-1", "gpt-test");

        result.Allowed.Should().BeTrue();
        result.PriceVersion.Should().Be("price-v1");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task CheckBalanceAndModel_ZeroBalance_ReturnsInsufficientBalance()
    {
        var service = await CreateServiceWithModelAsync();

        var result = await service.CheckBalanceAndModelAsync("user-1", "gpt-test");

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InsufficientBalance);
    }

    [Fact]
    public async Task CheckBalanceAndModel_UnknownModel_ReturnsModelUnavailable()
    {
        var service = CreateService();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.CheckBalanceAndModelAsync("user-1", "unknown");

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ModelUnavailable);
    }

    [Fact]
    public async Task ListPublicModels_OnlyEnabledModels_ReturnsEnabledModels()
    {
        var service = CreateService();
        await service.UpsertModelAsync(CreateModel("enabled", enabled: true));
        await service.UpsertModelAsync(CreateModel("disabled", enabled: false));

        var models = await service.ListPublicModelsAsync();

        var model = models.Should().ContainSingle().Which;
        model.Id.Should().Be("enabled");
    }

    [Fact]
    public async Task RecordUsage_ValidUsage_CreatesChargeAndDecreasesBalance()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-1", new TokenUsage(1_000_000, 1_000_000, 0)), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Rub(70));
    }

    [Fact]
    public async Task RecordUsage_SameRequestId_DoesNotChargeTwice()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usage = CreateUsageEvent("req-1", new TokenUsage(1_000_000, 1_000_000, 0));

        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);
        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);

        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Rub(70));
    }

    [Fact]
    public async Task RecordUsage_ValidUsage_WritesActivityByToken()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        await service.RecordUsageAsync(CreateUsageEvent("req-activity", new TokenUsage(10, 20, 3)), DateTimeOffset.UtcNow);

        var activity = (await service.ListActivityByTokenAsync(["key-1"])).Should().ContainSingle().Which;
        activity.RequestId.Should().Be("req-activity");
        activity.ApiKeyId.Should().Be("key-1");
        activity.ApiKeyPrefix.Should().Be("sk-or-test");
        activity.Usage.InputTokens.Should().Be(10);
        activity.Usage.OutputTokens.Should().Be(20);
        activity.Usage.CacheTokens.Should().Be(3);
    }

    [Fact]
    public async Task RecordUsage_UsageMissing_WritesActivityWithoutCharge()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-missing", new TokenUsage(0, 0, 0), "usage_missing"), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Rub(100));
        var activity = (await service.ListActivityByTokenAsync(["key-1"])).Should().ContainSingle().Which;
        activity.Status.Should().Be("usage_missing");
        activity.Usage.TotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task RecordUsage_SameRequestId_DoesNotDuplicateActivity()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usage = CreateUsageEvent("req-idempotent", new TokenUsage(1, 1, 0));

        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);
        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);

        (await service.ListActivityByTokenAsync(["key-1"])).Should().ContainSingle();
    }

    [Fact]
    public async Task RecordUsage_UnknownModel_CreatesAnomaly()
    {
        var service = CreateService();

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-1", new TokenUsage(1, 1, 0)), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Cost.Should().Be(MoneyAmount.Rub(0));
        (await service.ListAnomaliesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RecordUsage_NegativeTokenCount_CreatesAnomaly()
    {
        var service = await CreateServiceWithModelAsync();

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-1", new TokenUsage(-1, 1, 0)), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_token_count");
        (await service.ListAnomaliesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ListTopUps_AfterTopUps_ReturnsTopUpsForUser()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "first", "key-1", now);
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(200), "second", "key-2", now.AddSeconds(1));

        var topUps = await service.ListTopUpsAsync("user-1");

        topUps.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListTopUps_OtherUserTopUps_NotReturned()
    {
        var service = CreateService();
        await service.CreateVirtualTopUpAsync("admin-1", "user-2", MoneyAmount.Rub(100), "other", "key-3", DateTimeOffset.UtcNow);

        var topUps = await service.ListTopUpsAsync("user-1");

        topUps.Should().BeEmpty();
    }

    private static BillingApplicationService CreateService()
    {
        return new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);
    }

    private static async Task<BillingApplicationService> CreateServiceWithModelAsync()
    {
        var service = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));
        return service;
    }

    private static ModelPrice CreateModel(string modelId, bool enabled)
    {
        return new ModelPrice(
            modelId,
            modelId,
            $"router/{modelId}",
            "price-v1",
            new ModelTokenPrice(MoneyAmount.Rub(10), MoneyAmount.Rub(20), MoneyAmount.Rub(1)),
            enabled);
    }

    private static UsageEvent CreateUsageEvent(string requestId, TokenUsage usage, string status = "succeeded")
    {
        var now = DateTimeOffset.UtcNow;
        return new UsageEvent(
            requestId,
            "user-1",
            "key-1",
            "sk-or-test",
            "gpt-test",
            "router-1",
            usage,
            status,
            now.AddSeconds(-1),
            now);
    }
}
