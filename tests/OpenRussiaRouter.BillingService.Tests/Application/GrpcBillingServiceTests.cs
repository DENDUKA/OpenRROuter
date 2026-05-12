using OpenRussiaRouter.BillingService.Application;
using OpenRussiaRouter.BillingService.Domain;
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

    private static BillingApplicationService CreateBillingWithModelAndBalance()
    {
        var billing = new BillingApplicationService(new InMemoryBillingStore());
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
