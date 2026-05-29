using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.BillingService.Tests.Infrastructure.Persistence;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class GrpcBillingServicePostgresIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task BillingGrpcServer_PostgresWorkflow_PersistsBalanceAndUsage()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        if (database is null)
        {
            return;
        }

        await using var factory = CreateFactory(database.ConnectionString);
        var client = CreateClient(factory);

        _ = await client.UpsertModelPriceAsync(new UpsertModelPriceRequest
        {
            ModelId = "gpt-pg",
            DisplayName = "GPT PG",
            UpstreamModelId = "router/gpt-pg",
            PriceVersion = "v1",
            InputPricePer1MUsd = "10",
            OutputPricePer1MUsd = "20",
            CachePricePer1MUsd = "1",
            Enabled = true
        });
        _ = await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-pg",
            AmountUsd = "50",
            Reason = "pg test",
            IdempotencyKey = "pg-topup-1"
        });
        _ = await client.RecordUsageAsync(new RecordUsageRequest
        {
            RequestId = "pg-req-1",
            UserId = "user-pg",
            ApiKeyId = "key-pg",
            ModelId = "router/gpt-pg",
            RouterNodeId = "node-pg",
            Usage = new TokenUsage { InputTokens = 1_000_000 },
            Status = "succeeded",
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),
            CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        });

        var balance = await client.GetBalanceAsync(new GetBalanceRequest { UserId = "user-pg" });
        var usage = await client.ListUsageAsync(new ListUsageRequest());

        Assert.Equal("40", balance.BalanceUsd);
        Assert.Equal("pg-req-1", Assert.Single(usage.Records).RequestId);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BillingDb"] = connectionString
                })));
    }

    private static global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient(channel);
    }
}
