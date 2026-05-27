using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class GrpcBillingServiceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public GrpcBillingServiceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task BillingGrpcServer_AdminAndUsageWorkflow_ReturnsExpectedResponses()
    {
        var client = CreateClient();

        var model = await client.UpsertModelPriceAsync(new UpsertModelPriceRequest
        {
            ModelId = "gpt-test",
            DisplayName = "GPT Test",
            UpstreamModelId = "router/gpt-test",
            PriceVersion = "v1",
            InputPricePer1MRub = "10",
            OutputPricePer1MRub = "20",
            CachePricePer1MRub = "1",
            Enabled = true
        });
        var topUp = await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-1",
            AmountRub = "100",
            Reason = "test",
            IdempotencyKey = "topup-1"
        });
        var check = await client.CheckBalanceAndModelAsync(new CheckBalanceAndModelRequest
        {
            UserId = "user-1",
            ModelId = "router/gpt-test"
        });
        var usage = await client.RecordUsageAsync(new RecordUsageRequest
        {
            RequestId = "req-1",
            UserId = "user-1",
            ApiKeyId = "key-1",
            ModelId = "router/gpt-test",
            RouterNodeId = "node-1",
            Usage = new TokenUsage { InputTokens = 1_000_000 },
            Status = "succeeded",
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        });
        var records = await client.ListUsageAsync(new ListUsageRequest());

        Assert.True(model.Upserted);
        Assert.True(topUp.Created);
        Assert.True(check.Allowed);
        Assert.True(usage.Accepted);
        Assert.Equal("req-1", Assert.Single(records.Records).RequestId);
    }

    private global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient CreateClient()
    {
        var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SqlMigrations:Enabled"] = "false"
                })));
        var httpClient = configuredFactory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient(channel);
    }
}
