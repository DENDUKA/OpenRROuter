using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
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
    public async Task CreateVirtualTopUp_PositiveUsd_IncreasesBalance()
    {
        var service = CreateService();

        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Usd(100));
    }

    [Fact]
    public async Task CreateVirtualTopUp_SameIdempotencyKey_DoesNotIncreaseBalanceTwice()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;

        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", now);
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", now);

        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Usd(100));
    }

    [Fact]
    public async Task CheckBalanceAndModel_PositiveBalanceAndEnabledModel_ReturnsAllowed()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.CheckBalanceAndModelAsync("user-1", "router/gpt-test");

        result.Allowed.Should().BeTrue();
        result.PriceVersion.Should().Be("price-v1");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task CheckBalanceAndModel_ZeroBalance_ReturnsInsufficientBalance()
    {
        var service = await CreateServiceWithModelAsync();

        var result = await service.CheckBalanceAndModelAsync("user-1", "router/gpt-test");

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InsufficientBalance);
    }

    [Fact]
    public async Task CheckBalanceAndModel_DailyLimitReached_ReturnsApiKeyLimitExceeded()
    {
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", now);
        await service.UpsertApiKeySpendLimitAsync("user-1", "key-1", MoneyAmount.Usd(10), null, now);
        await service.RecordUsageAsync(CreateUsageEvent("req-daily-limit", new TokenUsage(1_000_000, 0, 0)), now);

        var result = await service.CheckBalanceAndModelAsync("user-1", "router/gpt-test", apiKeyId: "key-1", nowUtc: now);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ApiKeyLimitExceeded);
    }

    [Fact]
    public async Task CheckBalanceAndModel_WeeklyLimitReached_ReturnsApiKeyLimitExceeded()
    {
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", now);
        await service.UpsertApiKeySpendLimitAsync("user-1", "key-1", null, MoneyAmount.Usd(10), now);
        await service.RecordUsageAsync(CreateUsageEvent("req-weekly-limit", new TokenUsage(1_000_000, 0, 0)), now);

        var result = await service.CheckBalanceAndModelAsync("user-1", "router/gpt-test", apiKeyId: "key-1", nowUtc: now);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ApiKeyLimitExceeded);
    }

    [Fact]
    public async Task ListApiKeyLimitSummaries_UsesUtcDayAndWeekWindows()
    {
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", now);
        await service.UpsertApiKeySpendLimitAsync("user-1", "key-1", MoneyAmount.Usd(25), MoneyAmount.Usd(40), now);
        await service.RecordUsageAsync(CreateUsageEvent("req-yesterday", new TokenUsage(1_000_000, 0, 0)), now.AddDays(-1));
        await service.RecordUsageAsync(CreateUsageEvent("req-today", new TokenUsage(1_000_000, 0, 0)), now);

        var summary = (await service.ListApiKeyLimitSummariesAsync("user-1", ["key-1"], now)).Should().ContainSingle().Which;

        summary.DailySpent.Amount.Should().Be(10);
        summary.WeeklySpent.Amount.Should().Be(20);
        summary.DailyRemaining.Amount.Should().Be(15);
        summary.WeeklyRemaining.Amount.Should().Be(20);
        summary.DailyExceeded.Should().BeFalse();
        summary.WeeklyExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertApiKeySpendLimit_BothLimitsNull_ClearsSettings()
    {
        var now = DateTimeOffset.UtcNow;
        var service = CreateService();
        await service.UpsertApiKeySpendLimitAsync("user-1", "key-1", MoneyAmount.Usd(5), MoneyAmount.Usd(10), now);

        await service.UpsertApiKeySpendLimitAsync("user-1", "key-1", null, null, now.AddMinutes(1));

        (await service.GetApiKeySpendLimitAsync("user-1", "key-1")).Should().BeNull();
        var summary = (await service.ListApiKeyLimitSummariesAsync("user-1", ["key-1"], now)).Should().ContainSingle().Which;
        summary.DailyLimit.Should().BeNull();
        summary.WeeklyLimit.Should().BeNull();
    }

    [Fact]
    public async Task CheckBalanceAndModel_UnknownModel_ReturnsModelUnavailable()
    {
        var service = CreateService();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

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
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-1", new TokenUsage(1_000_000, 1_000_000, 0)), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Usd(70));
    }

    [Fact]
    public async Task RecordUsage_SameRequestId_DoesNotChargeTwice()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usage = CreateUsageEvent("req-1", new TokenUsage(1_000_000, 1_000_000, 0));

        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);
        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);

        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Usd(70));
    }

    [Fact]
    public async Task RecordUsage_ValidUsage_WritesActivityByToken()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        await service.RecordUsageAsync(CreateUsageEvent("req-activity", new TokenUsage(10, 20, 3)), DateTimeOffset.UtcNow);

        var activity = (await service.ListActivityByTokenAsync(["key-1"])).Items.Should().ContainSingle().Which;
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
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-missing", new TokenUsage(0, 0, 0), "usage_missing"), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        (await service.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Usd(100));
        var activity = (await service.ListActivityByTokenAsync(["key-1"])).Items.Should().ContainSingle().Which;
        activity.Status.Should().Be("usage_missing");
        activity.Usage.TotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task RecordUsage_SameRequestId_DoesNotDuplicateActivity()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
        var usage = CreateUsageEvent("req-idempotent", new TokenUsage(1, 1, 0));

        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);
        await service.RecordUsageAsync(usage, DateTimeOffset.UtcNow);

        (await service.ListActivityByTokenAsync(["key-1"])).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordUsage_UnknownModel_CreatesAnomaly()
    {
        var service = CreateService();

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-1", new TokenUsage(1, 1, 0)), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Cost.Should().Be(MoneyAmount.Usd(0));
        (await service.ListAnomaliesAsync()).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordUsage_NegativeTokenCount_CreatesAnomaly()
    {
        var service = await CreateServiceWithModelAsync();

        var result = await service.RecordUsageAsync(CreateUsageEvent("req-1", new TokenUsage(-1, 1, 0)), DateTimeOffset.UtcNow);

        result.Accepted.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_token_count");
        (await service.ListAnomaliesAsync()).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task ListTopUps_AfterTopUps_ReturnsTopUpsForUser()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "first", "key-1", now);
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(200), "second", "key-2", now.AddSeconds(1));

        var topUps = await service.ListTopUpsAsync("user-1");

        topUps.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListTopUps_OtherUserTopUps_NotReturned()
    {
        var service = CreateService();
        await service.CreateVirtualTopUpAsync("admin-1", "user-2", MoneyAmount.Usd(100), "other", "key-3", DateTimeOffset.UtcNow);

        var topUps = await service.ListTopUpsAsync("user-1");

        topUps.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertTag_InvalidId_Throws()
    {
        var service = CreateService();

        var act = async () => await service.UpsertTagAsync(new Tag("bad tag", "Bad Tag", "#0d6efd"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertTag_TrimsAndStoresTag()
    {
        var service = CreateService();

        await service.UpsertTagAsync(new Tag(" fast ", " Fast ", " #0d6efd "));

        var tag = (await service.ListTagsAsync()).Should().ContainSingle().Which;
        tag.Id.Should().Be("fast");
        tag.DisplayName.Should().Be("Fast");
        tag.Color.Should().Be("#0d6efd");
    }

    [Fact]
    public async Task DeleteTag_RemovesTagFromModels()
    {
        var service = CreateService();
        await service.UpsertTagAsync(new Tag("fast", "Fast", "#0d6efd"));
        await service.UpsertTagAsync(new Tag("cheap", "Cheap", "#198754"));
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true) with { TagIds = ["fast", "cheap"] });

        await service.DeleteTagAsync("fast");

        var model = (await service.ListAllModelsAsync()).Should().ContainSingle().Which;
        model.TagIds.Should().Equal("cheap");
        (await service.ListTagsAsync()).Should().ContainSingle(t => t.Id == "cheap");
    }

    [Fact]
    public async Task UpsertModelAsync_FlagshipWithoutCompleteOpenRouterPrices_ThrowsAsync()
    {
        var service = CreateService();
        var model = CreateModel("gpt-flagship", enabled: true) with
        {
            IsFlagship = true,
            OpenRouterInputPrice = 8m,
            OpenRouterOutputPrice = 18m
        };

        var act = () => service.UpsertModelAsync(model);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertModelAsync_FlagshipWithCompleteOpenRouterPrices_PreservesPricesAsync()
    {
        var service = CreateService();
        var model = CreateModel("gpt-flagship", enabled: true) with
        {
            IsFlagship = true,
            OpenRouterInputPrice = 0m,
            OpenRouterOutputPrice = 15m,
            OpenRouterCachePrice = 0.5m
        };

        await service.UpsertModelAsync(model);

        var stored = (await service.ListAllModelsAsync()).Should().ContainSingle().Which;
        stored.OpenRouterInputPrice.Should().Be(0m);
        stored.OpenRouterOutputPrice.Should().Be(15m);
        stored.OpenRouterCachePrice.Should().Be(0.5m);
    }

    [Fact]
    public async Task UpsertModelAsync_OpenRouterPriceBelowPlatformPrice_DoesNotBlockSaveAsync()
    {
        var service = CreateService();
        var model = CreateModel("gpt-flagship", enabled: true) with
        {
            IsFlagship = true,
            OpenRouterInputPrice = 1m,
            OpenRouterOutputPrice = 2m,
            OpenRouterCachePrice = 0.1m
        };

        var act = () => service.UpsertModelAsync(model);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpsertModelAsync_NegativeOpenRouterPrice_ThrowsAsync()
    {
        var service = CreateService();
        var model = CreateModel("gpt-standard", enabled: true) with
        {
            OpenRouterInputPrice = -0.000001m
        };

        var act = () => service.UpsertModelAsync(model);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task UpsertModel_DefaultReasoningEffortOutsideAllowed_Throws()
    {
        var service = CreateService();

        var act = async () => await service.UpsertModelAsync(CreateModel("gpt-reasoning", enabled: true) with
        {
            SupportsReasoning = true,
            AllowedReasoningEfforts = ["low", "medium"],
            DefaultReasoningEffort = "high"
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertModel_InvalidReasoningEffort_Throws()
    {
        var service = CreateService();

        var act = async () => await service.UpsertModelAsync(CreateModel("gpt-reasoning", enabled: true) with
        {
            SupportsReasoning = true,
            AllowedReasoningEfforts = ["super-ultra-unsupported"]
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateVirtualTopUp_NegativeAmount_Throws()
    {
        var service = CreateService();

        var act = async () => await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(-100), "test", "topup-1", DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateVirtualTopUp_ZeroAmount_Throws()
    {
        var service = CreateService();

        var act = async () => await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(0), "test", "topup-1", DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CheckBalanceAndModel_DisabledModel_ReturnsUnavailable()
    {
        var service = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: false));
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.CheckBalanceAndModelAsync("user-1", "router/gpt-test");

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ModelUnavailable);
    }

    [Fact]
    public async Task RecordUsage_LargeTokenCount_PreciseRounding()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(1000), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.RecordUsageAsync(
            CreateUsageEvent("req-large", new TokenUsage(999_999, 999_999, 0)),
            DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        result.Cost.Amount.Should().Be(29.99997m);
    }

    [Fact]
    public async Task UpsertModel_UpdateExistingPrice_ChangesPrices()
    {
        var service = CreateService();
        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));

        var updatedModel = CreateModel("gpt-test", enabled: true) with
        {
            Price = new ModelTokenPrice(MoneyAmount.Usd(20), MoneyAmount.Usd(40), MoneyAmount.Usd(2))
        };
        await service.UpsertModelAsync(updatedModel);

        var models = await service.ListAllModelsAsync();
        var model = models.Should().ContainSingle().Which;
        model.Price.InputPerMillion.Amount.Should().Be(20m);
        model.Price.OutputPerMillion.Amount.Should().Be(40m);
    }

    [Fact]
    public async Task RecordUsage_CacheTokens_CalculatedCorrectly()
    {
        var service = await CreateServiceWithModelAsync();
        await service.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

        var result = await service.RecordUsageAsync(
            CreateUsageEvent("req-cache", new TokenUsage(1_000_000, 0, 500_000)),
            DateTimeOffset.UtcNow);

        result.Accepted.Should().BeTrue();
        result.Cost.Amount.Should().Be(5.5m);
    }

    [Fact]
    public async Task DeleteTag_NonExistentTag_NoException()
    {
        var service = CreateService();

        var act = async () => await service.DeleteTagAsync("non-existent");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpsertModel_WithTags_ModelHasTagIds()
    {
        var service = CreateService();
        await service.UpsertTagAsync(new Tag("fast", "Fast", "#0d6efd"));
        await service.UpsertTagAsync(new Tag("cheap", "Cheap", "#198754"));

        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true) with { TagIds = ["fast", "cheap"] });

        var model = (await service.ListAllModelsAsync()).Should().ContainSingle().Which;
        model.TagIds.Should().Equal("fast", "cheap");
    }

    [Fact]
    public async Task InMemoryRepository_RoundTripsReasoningCapability()
    {
        var repository = new InMemoryBillingRepository();
        var model = CreateModel("gpt-reasoning", enabled: true) with
        {
            SupportsReasoning = true,
            AllowedReasoningEfforts = ["low", "medium", "high", "max"],
            DefaultReasoningEffort = "max"
        };

        await repository.UpsertModelAsync(model);

        var saved = await repository.GetModelAsync("router/gpt-reasoning");
        saved.Should().NotBeNull();
        saved!.SupportsReasoning.Should().BeTrue();
        saved.AllowedReasoningEfforts.Should().Equal("low", "medium", "high", "max");
        saved.DefaultReasoningEffort.Should().Be("max");
    }

    [Fact]
    public async Task RecordUsageAsync_FirstUsage_NotifiesTelegramBot()
    {
        var repo = new InMemoryBillingRepository();
        var fakeNotifier = new RecordingTelegramBotNotifier();
        var service = new BillingApplicationService(
            repo,
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance,
            telegramBotNotifier: fakeNotifier);

        await service.UpsertModelAsync(CreateModel("gpt-test", enabled: true));

        var evt = CreateUsageEvent("req-1", new TokenUsage(100, 50, 10));
        await service.RecordUsageAsync(evt, DateTimeOffset.UtcNow);

        fakeNotifier.Calls.Should().ContainSingle(c => c.UserId == "user-1" && c.ModelId == "router/gpt-test");
    }

    [Fact]
    public async Task GetDailyStatsAsync_ReturnsCountsAndSums()
    {
        var repo = new InMemoryBillingRepository();
        var service = new BillingApplicationService(
            repo,
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);

        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var (count, rub, usd, activeApiUsers) = await service.GetDailyStatsAsync(since);
        count.Should().Be(0);
        rub.Should().Be(0m);
        usd.Should().Be(0m);
        activeApiUsers.Should().Be(0);

        // Add usage records for two distinct users, and one duplicate user
        await repo.RecordUsageWithTransactionAsync(
            new ActivityByTokenRecord("req-1", "key-1", "sk-1", "gpt-4", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(1), since.AddHours(1)),
            new UsageRecord("req-1", "user-1", "key-1", "gpt-4", "v1", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(1)),
            null);
        await repo.RecordUsageWithTransactionAsync(
            new ActivityByTokenRecord("req-2", "key-1", "sk-1", "gpt-4", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(2), since.AddHours(2)),
            new UsageRecord("req-2", "user-1", "key-1", "gpt-4", "v1", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(2)),
            null);
        await repo.RecordUsageWithTransactionAsync(
            new ActivityByTokenRecord("req-3", "key-2", "sk-2", "gpt-4", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(3), since.AddHours(3)),
            new UsageRecord("req-3", "user-2", "key-2", "gpt-4", "v1", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(3)),
            null);
        // Add one older usage record outside window
        await repo.RecordUsageWithTransactionAsync(
            new ActivityByTokenRecord("req-4", "key-3", "sk-3", "gpt-4", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(-1), since.AddHours(-1)),
            new UsageRecord("req-4", "user-3", "key-3", "gpt-4", "v1", "node-1", new TokenUsage(10, 20, 0), MoneyAmount.Usd(0.01m), "completed", since.AddHours(-1)),
            null);

        var stats = await service.GetDailyStatsAsync(since);
        stats.ActiveApiUsers.Should().Be(2);
    }

    private sealed class RecordingTelegramBotNotifier : OpenRussiaRouter.BillingService.Infrastructure.Telegram.ITelegramBotNotifier
    {
        public List<(string UserId, string ModelId)> Calls { get; } = [];
        public void NotifyFirstApiRequest(string userId, string modelId) => Calls.Add((userId, modelId));
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
            new ModelTokenPrice(MoneyAmount.Usd(10), MoneyAmount.Usd(20), MoneyAmount.Usd(1)),
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
            "router/gpt-test",
            "router-1",
            usage,
            status,
            now.AddSeconds(-1),
            now);
    }
}
