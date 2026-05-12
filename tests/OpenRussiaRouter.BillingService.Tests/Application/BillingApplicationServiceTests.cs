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
    public void CreateVirtualTopUp_PositiveRub_IncreasesBalance()
    {
        var service = CreateService();

        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        Assert.Equal(MoneyAmount.Rub(100), service.GetBalance("user-1"));
    }

    [Fact]
    public void CreateVirtualTopUp_SameIdempotencyKey_DoesNotIncreaseBalanceTwice()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;

        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", now);
        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", now);

        Assert.Equal(MoneyAmount.Rub(100), service.GetBalance("user-1"));
    }

    [Fact]
    public void CheckBalanceAndModel_PositiveBalanceAndEnabledModel_ReturnsAllowed()
    {
        var service = CreateServiceWithModel();
        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = service.CheckBalanceAndModel("user-1", "gpt-test");

        Assert.True(result.Allowed);
        Assert.Equal("price-v1", result.PriceVersion);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void CheckBalanceAndModel_ZeroBalance_ReturnsInsufficientBalance()
    {
        var service = CreateServiceWithModel();

        var result = service.CheckBalanceAndModel("user-1", "gpt-test");

        Assert.False(result.Allowed);
        Assert.Equal(ErrorCodes.InsufficientBalance, result.ErrorCode);
    }

    [Fact]
    public void CheckBalanceAndModel_UnknownModel_ReturnsModelUnavailable()
    {
        var service = CreateService();
        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = service.CheckBalanceAndModel("user-1", "unknown");

        Assert.False(result.Allowed);
        Assert.Equal(ErrorCodes.ModelUnavailable, result.ErrorCode);
    }

    [Fact]
    public void ListPublicModels_OnlyEnabledModels_ReturnsEnabledModels()
    {
        var service = CreateService();
        service.UpsertModel(CreateModel("enabled", enabled: true));
        service.UpsertModel(CreateModel("disabled", enabled: false));

        var models = service.ListPublicModels();

        var model = Assert.Single(models);
        Assert.Equal("enabled", model.Id);
    }

    [Fact]
    public void RecordUsage_ValidUsage_CreatesChargeAndDecreasesBalance()
    {
        var service = CreateServiceWithModel();
        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = service.RecordUsage(CreateUsageEvent("req-1", new TokenUsage(1_000_000, 1_000_000, 0)), DateTimeOffset.UtcNow);

        Assert.True(result.Accepted);
        Assert.Equal(MoneyAmount.Rub(70), service.GetBalance("user-1"));
    }

    [Fact]
    public void RecordUsage_SameRequestId_DoesNotChargeTwice()
    {
        var service = CreateServiceWithModel();
        service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usage = CreateUsageEvent("req-1", new TokenUsage(1_000_000, 1_000_000, 0));

        service.RecordUsage(usage, DateTimeOffset.UtcNow);
        service.RecordUsage(usage, DateTimeOffset.UtcNow);

        Assert.Equal(MoneyAmount.Rub(70), service.GetBalance("user-1"));
    }

    [Fact]
    public void RecordUsage_UnknownModel_CreatesAnomaly()
    {
        var service = CreateService();

        var result = service.RecordUsage(CreateUsageEvent("req-1", new TokenUsage(1, 1, 0)), DateTimeOffset.UtcNow);

        Assert.False(result.Accepted);
        Assert.Equal("model_price_missing", result.ErrorCode);
        Assert.Single(service.ListAnomalies());
    }

    [Fact]
    public void RecordUsage_NegativeTokenCount_CreatesAnomaly()
    {
        var service = CreateServiceWithModel();

        var result = service.RecordUsage(CreateUsageEvent("req-1", new TokenUsage(-1, 1, 0)), DateTimeOffset.UtcNow);

        Assert.False(result.Accepted);
        Assert.Equal("invalid_token_count", result.ErrorCode);
        Assert.Single(service.ListAnomalies());
    }

    private static BillingApplicationService CreateService()
    {
        return new BillingApplicationService(new InMemoryBillingRepository());
    }

    private static BillingApplicationService CreateServiceWithModel()
    {
        var service = CreateService();
        service.UpsertModel(CreateModel("gpt-test", enabled: true));
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

    private static UsageEvent CreateUsageEvent(string requestId, TokenUsage usage)
    {
        var now = DateTimeOffset.UtcNow;
        return new UsageEvent(
            requestId,
            "user-1",
            "key-1",
            "gpt-test",
            "router-1",
            usage,
            "succeeded",
            now.AddSeconds(-1),
            now);
    }
}

