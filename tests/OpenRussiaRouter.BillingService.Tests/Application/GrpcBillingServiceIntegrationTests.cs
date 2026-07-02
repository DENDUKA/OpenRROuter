using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Infrastructure.Auth;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.BillingService.Tests.Application;

/// <summary>
/// Integration tests that verify the s2s auth interceptor is wired up correctly.
/// Each test creates its own <see cref="WebApplicationFactory{TEntryPoint}"/> so
/// the test host is started fresh with a guaranteed-correct configuration.
/// </summary>
public sealed class GrpcBillingServiceIntegrationTests
{
    [Fact]
    public async Task BillingGrpcServer_AdminAndUsageWorkflow_ReturnsExpectedResponses()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var model = await client.UpsertModelPriceAsync(new UpsertModelPriceRequest
        {
            ModelId = "gpt-test",
            DisplayName = "GPT Test",
            UpstreamModelId = "router/gpt-test",
            PriceVersion = "v1",
            InputPricePer1MUsd = "10",
            OutputPricePer1MUsd = "20",
            CachePricePer1MUsd = "1",
            Enabled = true
        }, S2sHeaders("webappadmin", S2sTestTokens.WebappAdmin));
        var topUp = await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest
        {
            AdminId = "admin-1",
            UserId = "user-1",
            AmountUsd = "100",
            Reason = "test",
            IdempotencyKey = "topup-1"
        }, S2sHeaders("webappadmin", S2sTestTokens.WebappAdmin));
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
        }, S2sHeaders("apigateway", S2sTestTokens.ApiGateway));
        var records = await client.ListUsageAsync(new ListUsageRequest());

        Assert.True(model.Upserted);
        Assert.True(topUp.Created);
        Assert.True(check.Allowed);
        Assert.True(usage.Accepted);
        Assert.Equal("req-1", Assert.Single(records.Records).RequestId);
    }

    [Theory]
    [InlineData("RecordUsage")]
    [InlineData("CreateVirtualTopUp")]
    [InlineData("UpsertApiKeyLimitSettings")]
    [InlineData("UpsertModelPrice")]
    [InlineData("UpsertTag")]
    [InlineData("DeleteTag")]
    [InlineData("CreatePayment")]
    [InlineData("AttachProviderRef")]
    [InlineData("ConfirmPaymentByProvider")]
    [InlineData("ApplyPaymentEvent")]
    [InlineData("AcquireStalePayments")]
    [InlineData("UpsertModelProvider")]
    public async Task MutatingMethod_WithoutS2sToken_ReturnsUnauthenticated(string methodName)
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await InvokeMutatingMethodWithoutAuthAsync(client, methodName));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task RecordUsage_WithMissingMethodPolicy_FailsClosed()
    {
        await using var factory = BuildFactory(opts => opts.MethodPolicies.Remove("RecordUsage"));
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.RecordUsageAsync(new RecordUsageRequest
            {
                RequestId = "req-missing-policy",
                UserId = "user-1",
                ApiKeyId = "key-1",
                ModelId = "router/gpt-test",
                RouterNodeId = "node-1",
                Usage = new TokenUsage { InputTokens = 1 },
                Status = "succeeded",
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),
                CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O")
            }, S2sHeaders("apigateway", S2sTestTokens.ApiGateway)));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task CreateVirtualTopUp_WithWrongCaller_ReturnsPermissionDenied()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest
            {
                AdminId = "admin-1",
                UserId = "user-1",
                AmountUsd = "5",
                Reason = "test",
                IdempotencyKey = "topup-wrong"
            }, S2sHeaders("apigateway", S2sTestTokens.ApiGateway)));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task CreateVirtualTopUp_WithUnknownCaller_ReturnsUnauthenticated()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest
            {
                AdminId = "admin-1",
                UserId = "user-1",
                AmountUsd = "5",
                Reason = "test",
                IdempotencyKey = "topup-unknown"
            }, S2sHeaders("rogue-caller", "rogue-token")));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task CreateVirtualTopUp_WithTokenMismatch_ReturnsUnauthenticated()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest
            {
                AdminId = "admin-1",
                UserId = "user-1",
                AmountUsd = "5",
                Reason = "test",
                IdempotencyKey = "topup-mismatch"
            }, S2sHeaders("webappadmin", "wrong-token")));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task ReadMethods_WithoutS2sToken_StillSucceed()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        // No metadata - read methods stay open.
        var models = await client.ListPublicModelsAsync(new ListPublicModelsRequest());
        Assert.NotNull(models);
    }

    private static WebApplicationFactory<Program> BuildFactory(Action<ServiceAuthOptions>? configureAuth = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SqlMigrations:Enabled"] = "false"
                }));
            // Authoritatively replace ServiceAuthOptions so we don't depend on appsettings.json
            // being copied to the test bin output.
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<ServiceAuthOptions>(opts =>
                {
                    opts.Tokens["apigateway"] = S2sTestTokens.ApiGateway;
                    opts.Tokens["webappadmin"] = S2sTestTokens.WebappAdmin;
                    opts.MethodPolicies["RecordUsage"] = new[] { "apigateway" };
                    opts.MethodPolicies["CreateVirtualTopUp"] = new[] { "webappadmin" };
                    opts.MethodPolicies["UpsertApiKeyLimitSettings"] = new[] { "webappadmin" };
                    opts.MethodPolicies["UpsertModelPrice"] = new[] { "webappadmin" };
                    opts.MethodPolicies["UpsertTag"] = new[] { "webappadmin" };
                    opts.MethodPolicies["DeleteTag"] = new[] { "webappadmin" };
                    opts.MethodPolicies["CreatePayment"] = new[] { "webappadmin" };
                    opts.MethodPolicies["AttachProviderRef"] = new[] { "webappadmin" };
                    opts.MethodPolicies["ConfirmPaymentByProvider"] = new[] { "webappadmin" };
                    opts.MethodPolicies["ApplyPaymentEvent"] = new[] { "webappadmin" };
                    opts.MethodPolicies["AcquireStalePayments"] = new[] { "webappadmin" };
                    configureAuth?.Invoke(opts);
                });
            });
        });
    }

    private static global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient(channel);
    }

    private static CallOptions S2sHeaders(string caller, string token)
    {
        var headers = new Metadata
        {
            { ServiceAuthOptions.CallerMetadataKey, caller },
            { ServiceAuthOptions.TokenMetadataKey, token }
        };
        return new CallOptions(headers: headers);
    }

    private static async Task InvokeMutatingMethodWithoutAuthAsync(
        global::OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService.BillingServiceClient client,
        string methodName)
    {
        switch (methodName)
        {
            case "RecordUsage":
                await client.RecordUsageAsync(new RecordUsageRequest());
                break;
            case "CreateVirtualTopUp":
                await client.CreateVirtualTopUpAsync(new CreateVirtualTopUpRequest());
                break;
            case "UpsertApiKeyLimitSettings":
                await client.UpsertApiKeyLimitSettingsAsync(new UpsertApiKeyLimitSettingsRequest());
                break;
            case "UpsertModelPrice":
                await client.UpsertModelPriceAsync(new UpsertModelPriceRequest());
                break;
            case "UpsertTag":
                await client.UpsertTagAsync(new UpsertTagRequest());
                break;
            case "DeleteTag":
                await client.DeleteTagAsync(new DeleteTagRequest());
                break;
            case "CreatePayment":
                await client.CreatePaymentAsync(new CreatePaymentRequest());
                break;
            case "AttachProviderRef":
                await client.AttachProviderRefAsync(new AttachProviderRefRequest());
                break;
            case "ConfirmPaymentByProvider":
                await client.ConfirmPaymentByProviderAsync(new ConfirmPaymentByProviderRequest());
                break;
            case "ApplyPaymentEvent":
                await client.ApplyPaymentEventAsync(new ApplyPaymentEventRequest());
                break;
            case "AcquireStalePayments":
                await client.AcquireStalePaymentsAsync(new AcquireStalePaymentsRequest());
                break;
            case "UpsertModelProvider":
                await client.UpsertModelProviderAsync(new UpsertProviderRequest());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null);
        }
    }
}
