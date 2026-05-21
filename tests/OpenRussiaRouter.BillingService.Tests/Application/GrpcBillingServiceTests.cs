using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
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
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);

        var response = await service.CheckBalanceAndModel(
            new CheckBalanceAndModelRequest { UserId = "user-1", ModelId = "gpt-test" },
            context: null!);

        Assert.True(response.Allowed);
        Assert.Equal("gpt-test", response.ModelId);
        Assert.Equal("price-v1", response.PriceVersion);
        Assert.Equal("100", response.BalanceRub);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task ListPublicModels_EnabledModels_ReturnsGrpcModels()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);

        var response = await service.ListPublicModels(new ListPublicModelsRequest(), context: null!);

        var model = Assert.Single(response.Models);
        Assert.Equal("gpt-test", model.Id);
        Assert.Equal("GPT Test", model.DisplayName);
    }

    [Fact]
    public async Task RecordUsage_ValidUsage_ReturnsAccepted()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);
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

        Assert.True(response.Accepted);
        Assert.Equal("req-1", response.RequestId);
        Assert.Equal(string.Empty, response.ErrorCode);
        Assert.Equal(MoneyAmount.Rub(90), billing.GetBalance("user-1"));
    }

    [Fact]
    public async Task GetBalance_ExistingUser_ReturnsBalanceRub()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);

        var response = await service.GetBalance(new GetBalanceRequest { UserId = "user-1" }, context: null!);

        Assert.Equal("user-1", response.UserId);
        Assert.Equal("100", response.BalanceRub);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task CreateVirtualTopUp_ValidRequest_ReturnsLedgerAndBalance()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }));
        var service = new GrpcBillingService(billing);

        var response = await service.CreateVirtualTopUp(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-1",
            AmountRub = "25.50",
            Reason = "manual top-up",
            IdempotencyKey = "topup-1"
        }, context: null!);

        Assert.True(response.Created);
        Assert.NotEmpty(response.LedgerEntryId);
        Assert.Equal("user-1", response.UserId);
        Assert.Equal("25.50", response.AmountRub);
        Assert.Equal("25.50", response.BalanceRub);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task CreateVirtualTopUp_InvalidAmount_ReturnsValidationFailed()
    {
        var service = new GrpcBillingService(new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 })));

        var response = await service.CreateVirtualTopUp(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-1",
            AmountRub = "0",
            Reason = "manual top-up",
            IdempotencyKey = "topup-1"
        }, context: null!);

        Assert.False(response.Created);
        Assert.Equal("validation_failed", response.ErrorCode);
    }

    [Fact]
    public async Task UpsertModelPrice_ValidRequest_ModelAppearsInPublicList()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }));
        var service = new GrpcBillingService(billing);

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

        Assert.True(response.Upserted);
        Assert.Equal("gpt-admin", response.ModelId);
        Assert.Equal("gpt-admin", Assert.Single(models.Models).Id);
    }

    [Fact]
    public async Task UpsertModelPrice_InvalidPrice_ReturnsValidationFailed()
    {
        var service = new GrpcBillingService(new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 })));

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

        Assert.False(response.Upserted);
        Assert.Equal("validation_failed", response.ErrorCode);
    }

    [Fact]
    public async Task ListBillingAnomalies_AfterInvalidUsage_ReturnsAnomaly()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);

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

        var anomaly = Assert.Single(response.Anomalies);
        Assert.Equal("req-invalid", anomaly.RequestId);
        Assert.Equal("invalid_token_count", anomaly.Type);
        Assert.NotEmpty(anomaly.CreatedAtUtc);
    }

    [Fact]
    public async Task ListUsage_AfterValidUsage_ReturnsUsageRecord()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);
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

        var record = Assert.Single(response.Records);
        Assert.Equal("req-usage", record.RequestId);
        Assert.Equal("user-1", record.UserId);
        Assert.Equal("gpt-test", record.ModelId);
        Assert.Equal("20", record.CostRub);
        Assert.Equal(1_000_000, record.Usage.InputTokens);
        Assert.Equal(500_000, record.Usage.OutputTokens);
    }

    [Fact]
    public async Task ListActivityByToken_ReturnsOnlyRequestedKeys()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);
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

        var record = Assert.Single(response.Records);
        Assert.Equal("req-key-1", record.RequestId);
        Assert.Equal("key-1", record.ApiKeyId);
        Assert.Equal("sk-or-key-1", record.ApiKeyPrefix);
        Assert.Equal(10, record.Usage.InputTokens);
        Assert.Equal(20, record.Usage.OutputTokens);
        Assert.Equal(3, record.Usage.CacheTokens);
    }

    [Fact]
    public async Task ListTopUps_ExistingTopUp_ReturnsRecord()
    {
        var billing = CreateBillingWithModelAndBalance();
        var service = new GrpcBillingService(billing);

        var response = await service.ListTopUps(
            new ListTopUpsRequest { UserId = "user-1" },
            context: null!);

        var record = Assert.Single(response.Records);
        Assert.NotEmpty(record.Id);
        Assert.Equal("100", record.AmountRub);
        Assert.Equal("test", record.Reason);
        Assert.NotEmpty(record.CreatedAtUtc);
    }

    private static BillingApplicationService CreateBillingWithModelAndBalance()
    {
        var billing = new BillingApplicationService(
            new InMemoryBillingRepository(),
            new UsageCostCalculator(),
            Options.Create(new BillingPaginationOptions { DefaultPageSize = 1000, MaxPageSize = 5000 }));
        billing.UpsertModel(new ModelPrice(
            "gpt-test",
            "GPT Test",
            "router/gpt-test",
            "price-v1",
            new ModelTokenPrice(MoneyAmount.Rub(10), MoneyAmount.Rub(20), MoneyAmount.Rub(1)),
            Enabled: true));
        billing.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", DateTimeOffset.UtcNow);
        return billing;
    }
}
