using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Infrastructure.Grpc;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class GrpcBillingServiceTests
{
    [Fact]
    public async Task CheckBalanceAndModel_Allowed_ReturnsGrpcResponse()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.CheckBalanceAndModel(
            new CheckBalanceAndModelRequest { UserId = "user-1", ModelId = "gpt-test" },
            context: null!);

        response.Allowed.Should().BeTrue();
        response.ModelId.Should().Be("gpt-test");
        response.PriceVersion.Should().Be("price-v1");
        response.BalanceRub.Should().Be("100");
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task ListPublicModels_EnabledModels_ReturnsGrpcModels()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.ListPublicModels(new ListPublicModelsRequest(), context: null!);

        var model = response.Models.Should().ContainSingle().Which;
        model.Id.Should().Be("gpt-test");
        model.DisplayName.Should().Be("GPT Test");
        model.UpstreamModelId.Should().Be("router/gpt-test");
        model.InputPricePer1MRub.Should().Be("10");
        model.OutputPricePer1MRub.Should().Be("20");
    }

    [Fact]
    public async Task RecordUsage_ValidUsage_ReturnsAccepted()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);
        var now = DateTimeOffset.UtcNow;

        var response = await service.RecordUsage(new RecordUsageRequest
        {
            RequestId = "req-1",
            UserId = "user-1",
            ApiKeyId = "key-1",
            ApiKeyPrefix = "sk-or-key-1",
            ModelId = "gpt-test",
            RouterNodeId = "router-1",
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage
            {
                InputTokens = 1_000_000,
                OutputTokens = 0,
                CacheTokens = 0
            },
            Status = "succeeded",
            StartedAtUtc = now.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = now.ToString("O")
        }, context: null!);

        response.Accepted.Should().BeTrue();
        response.RequestId.Should().Be("req-1");
        response.ErrorCode.Should().Be(string.Empty);
        (await billing.GetBalanceAsync("user-1")).Should().Be(MoneyAmount.Rub(90));
    }

    [Fact]
    public async Task GetBalance_ExistingUser_ReturnsBalanceRub()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.GetBalance(new GetBalanceRequest { UserId = "user-1" }, context: null!);

        response.UserId.Should().Be("user-1");
        response.BalanceRub.Should().Be("100");
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task CreateVirtualTopUp_ValidRequest_ReturnsLedgerAndBalance()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.CreateVirtualTopUp(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-1",
            AmountRub = "25.50",
            Reason = "manual top-up",
            IdempotencyKey = "topup-1"
        }, context: null!);

        response.Created.Should().BeTrue();
        response.LedgerEntryId.Should().NotBeNullOrEmpty();
        response.UserId.Should().Be("user-1");
        response.AmountRub.Should().Be("25.50");
        response.BalanceRub.Should().Be("25.50");
        response.ErrorCode.Should().Be(string.Empty);
    }

    [Fact]
    public async Task CreateVirtualTopUp_InvalidAmount_ReturnsValidationFailed()
    {
        var service = new GrpcBillingService(new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance),
            NullLogger<GrpcBillingService>.Instance);

        var response = await service.CreateVirtualTopUp(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-1",
            AmountRub = "0",
            Reason = "manual top-up",
            IdempotencyKey = "topup-1"
        }, context: null!);

        response.Created.Should().BeFalse();
        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task UpsertModelPrice_ValidRequest_ModelAppearsInPublicList()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.UpsertModelPrice(new UpsertModelPriceRequest
        {
            ModelId = "gpt-admin",
            DisplayName = "GPT Admin",
            UpstreamModelId = "router/gpt-admin",
            PriceVersion = "v1",
            InputPricePer1MRub = "10",
            OutputPricePer1MRub = "20",
            CachePricePer1MRub = "1",
            Enabled = true
        }, context: null!);
        var models = await service.ListPublicModels(new ListPublicModelsRequest(), context: null!);

        response.Upserted.Should().BeTrue();
        response.ModelId.Should().Be("gpt-admin");
        var model = models.Models.Should().ContainSingle().Which;
        model.Id.Should().Be("gpt-admin");
        model.UpstreamModelId.Should().Be("router/gpt-admin");
        model.InputPricePer1MRub.Should().Be("10");
        model.OutputPricePer1MRub.Should().Be("20");
    }

    [Fact]
    public async Task UpsertModelPrice_InvalidPrice_ReturnsValidationFailed()
    {
        var service = new GrpcBillingService(new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance),
            NullLogger<GrpcBillingService>.Instance);

        var response = await service.UpsertModelPrice(new UpsertModelPriceRequest
        {
            ModelId = "gpt-admin",
            DisplayName = "GPT Admin",
            UpstreamModelId = "router/gpt-admin",
            PriceVersion = "v1",
            InputPricePer1MRub = "bad",
            OutputPricePer1MRub = "20",
            CachePricePer1MRub = "1",
            Enabled = true
        }, context: null!);

        response.Upserted.Should().BeFalse();
        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task ListBillingAnomalies_AfterInvalidUsage_ReturnsAnomaly()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        _ = await service.RecordUsage(new RecordUsageRequest
        {
            RequestId = "req-invalid",
            UserId = "user-1",
            ApiKeyId = "key-1",
            ApiKeyPrefix = "sk-or-key-1",
            ModelId = "gpt-test",
            RouterNodeId = "router-1",
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage { InputTokens = -1 },
            Status = "failed",
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        }, context: null!);

        var response = await service.ListBillingAnomalies(new ListBillingAnomaliesRequest(), context: null!);

        var anomaly = response.Anomalies.Should().ContainSingle().Which;
        anomaly.RequestId.Should().Be("req-invalid");
        anomaly.Type.Should().Be("invalid_token_count");
        anomaly.CreatedAtUtc.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListUsage_AfterValidUsage_ReturnsUsageRecord()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);
        var now = DateTimeOffset.UtcNow;

        _ = await service.RecordUsage(new RecordUsageRequest
        {
            RequestId = "req-usage",
            UserId = "user-1",
            ApiKeyId = "key-1",
            ApiKeyPrefix = "sk-or-key-1",
            ModelId = "gpt-test",
            RouterNodeId = "router-1",
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage { InputTokens = 1_000_000, OutputTokens = 500_000 },
            Status = "succeeded",
            StartedAtUtc = now.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = now.ToString("O")
        }, context: null!);

        var response = await service.ListUsage(new ListUsageRequest(), context: null!);

        var record = response.Records.Should().ContainSingle().Which;
        record.RequestId.Should().Be("req-usage");
        record.UserId.Should().Be("user-1");
        record.ModelId.Should().Be("gpt-test");
        record.CostRub.Should().Be("20");
        record.Usage.InputTokens.Should().Be(1_000_000);
        record.Usage.OutputTokens.Should().Be(500_000);
    }

    [Fact]
    public async Task ListActivityByToken_ReturnsOnlyRequestedKeys()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);
        var now = DateTimeOffset.UtcNow;

        _ = await service.RecordUsage(new RecordUsageRequest
        {
            RequestId = "req-key-1",
            UserId = "user-1",
            ApiKeyId = "key-1",
            ApiKeyPrefix = "sk-or-key-1",
            ModelId = "gpt-test",
            RouterNodeId = "router-1",
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage { InputTokens = 10, OutputTokens = 20, CacheTokens = 3 },
            Status = "completed",
            StartedAtUtc = now.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = now.ToString("O")
        }, context: null!);

        _ = await service.RecordUsage(new RecordUsageRequest
        {
            RequestId = "req-key-2",
            UserId = "user-1",
            ApiKeyId = "key-2",
            ApiKeyPrefix = "sk-or-key-2",
            ModelId = "gpt-test",
            RouterNodeId = "router-1",
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage { InputTokens = 30, OutputTokens = 40, CacheTokens = 5 },
            Status = "completed",
            StartedAtUtc = now.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = now.ToString("O")
        }, context: null!);

        var response = await service.ListActivityByToken(new ListActivityByTokenRequest
        {
            ApiKeyIds = { "key-1" }
        }, context: null!);

        var record = response.Records.Should().ContainSingle().Which;
        record.RequestId.Should().Be("req-key-1");
        record.ApiKeyId.Should().Be("key-1");
        record.ApiKeyPrefix.Should().Be("sk-or-key-1");
        record.Usage.InputTokens.Should().Be(10);
        record.Usage.OutputTokens.Should().Be(20);
        record.Usage.CacheTokens.Should().Be(3);
    }

    [Fact]
    public async Task ListTopUps_ExistingTopUp_ReturnsRecord()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.ListTopUps(
            new ListTopUpsRequest { UserId = "user-1" },
            context: null!);

        var record = response.Records.Should().ContainSingle().Which;
        record.Id.Should().NotBeNullOrEmpty();
        record.AmountRub.Should().Be("100");
        record.Reason.Should().Be("test");
        record.CreatedAtUtc.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckBalanceAndModel_UnknownModel_ReturnsModelUnavailable()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.CheckBalanceAndModel(
            new CheckBalanceAndModelRequest { UserId = "user-1", ModelId = "nonexistent-model" },
            context: null!);

        response.Allowed.Should().BeFalse();
        response.ErrorCode.Should().Be("model_unavailable");
    }

    [Fact]
    public async Task CheckBalanceAndModel_ZeroBalance_ReturnsInsufficientBalance()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);
        await billing.UpsertModelAsync(new ModelPrice(
            "gpt-test", "GPT Test", "router/gpt-test", "v1",
            new ModelTokenPrice(MoneyAmount.Rub(10), MoneyAmount.Rub(20), MoneyAmount.Rub(1)),
            Enabled: true));
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.CheckBalanceAndModel(
            new CheckBalanceAndModelRequest { UserId = "user-1", ModelId = "gpt-test" },
            context: null!);

        response.Allowed.Should().BeFalse();
        response.ErrorCode.Should().Be("insufficient_balance");
    }

    [Fact]
    public async Task GetBalance_EmptyUserId_ReturnsValidationFailed()
    {
        var service = new GrpcBillingService(new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance),
            NullLogger<GrpcBillingService>.Instance);

        var response = await service.GetBalance(new GetBalanceRequest { UserId = "" }, context: null!);

        response.ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task RecordUsage_MalformedTimestamp_IsAccepted()
    {
        var billing = await CreateBillingWithModelAndBalanceAsync();
        var service = new GrpcBillingService(billing, NullLogger<GrpcBillingService>.Instance);

        var response = await service.RecordUsage(new RecordUsageRequest
        {
            RequestId = "req-bad-ts",
            UserId = "user-1",
            ApiKeyId = "key-1",
            ApiKeyPrefix = "sk-or-key-1",
            ModelId = "gpt-test",
            RouterNodeId = "router-1",
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage { InputTokens = 10, OutputTokens = 5 },
            Status = "succeeded",
            StartedAtUtc = "not-a-timestamp",
            CompletedAtUtc = "also-not-a-timestamp"
        }, context: null!);

        response.Accepted.Should().BeTrue();
        response.RequestId.Should().Be("req-bad-ts");
        response.ErrorCode.Should().Be(string.Empty);
    }

    private static async Task<BillingApplicationService> CreateBillingWithModelAndBalanceAsync()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }),
            NullLogger<BillingApplicationService>.Instance);
        await billing.UpsertModelAsync(new ModelPrice(
            "gpt-test",
            "GPT Test",
            "router/gpt-test",
            "price-v1",
            new ModelTokenPrice(MoneyAmount.Rub(10), MoneyAmount.Rub(20), MoneyAmount.Rub(1)),
            Enabled: true));
        await billing.CreateVirtualTopUpAsync("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);
        return billing;
    }
}
