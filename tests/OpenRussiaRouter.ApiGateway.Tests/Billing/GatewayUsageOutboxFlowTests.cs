using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class GatewayUsageOutboxFlowTests
{
    [Fact]
    public async Task ChatCompletions_UsageResponse_EnqueuesUsageInsteadOfDirectRecordUsage()
    {
        var usageOutbox = new CapturingUsageOutboxRepository();
        var billingClient = new RecordingBillingClient();
        using var factory = CreateFactory(usageOutbox, billingClient, new StaticRouterProxyClient(
            """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":20}}"""));
        var client = factory.CreateClient();
        var request = CreateChatRequest();

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("chatcmpl-1", body, StringComparison.Ordinal);
        Assert.Single(usageOutbox.Enqueued);
        Assert.Equal("user-1", Assert.Single(usageOutbox.Enqueued).UserId);
        Assert.Equal(0, billingClient.RecordUsageCalls);
    }

    [Fact]
    public async Task ChatCompletions_ZeroUsage_SkipsEnqueue()
    {
        var usageOutbox = new CapturingUsageOutboxRepository();
        using var factory = CreateFactory(usageOutbox, new RecordingBillingClient(), new StaticRouterProxyClient(
            """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":0,"completion_tokens":0}}"""));
        var client = factory.CreateClient();
        var request = CreateChatRequest();

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(usageOutbox.Enqueued);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IUsageOutboxRepository usageOutbox,
        IBillingGatewayClient billingClient,
        IRouterProxyClient routerProxyClient)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UsageOutbox:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApiKeyAuthenticator>();
                services.RemoveAll<IBillingGatewayClient>();
                services.RemoveAll<IControlPlaneGatewayClient>();
                services.RemoveAll<IRouterProxyClient>();
                services.RemoveAll<IUsageOutboxRepository>();

                services.AddScoped<IApiKeyAuthenticator, StaticAuthenticator>();
                services.AddScoped(_ => billingClient);
                services.AddScoped<IControlPlaneGatewayClient, StaticControlPlaneClient>();
                services.AddScoped(_ => routerProxyClient);
                services.AddSingleton(usageOutbox);
            });
        });
    }

    private static HttpRequestMessage CreateChatRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent("""{"model":"gpt-test","messages":[{"role":"user","content":"hello"}]}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        request.Headers.Add("X-Request-Id", "req-1");
        return request;
    }

    private sealed class StaticAuthenticator : IApiKeyAuthenticator
    {
        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(ApiKeyAuthenticationResult.Success("user-1", "key-1"));
        }
    }

    private sealed class RecordingBillingClient : IBillingGatewayClient
    {
        public int RecordUsageCalls { get; private set; }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<GatewayPublicModel>>([]);
        }

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GatewayBalanceCheckResult(true, modelId, "v1", MoneyAmount.Rub(100), null));
        }

        public Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
        {
            RecordUsageCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class StaticControlPlaneClient : IControlPlaneGatewayClient
    {
        public Task<GatewayRouterLease?> SelectRouterNodeAsync(string modelId, string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult<GatewayRouterLease?>(new GatewayRouterLease(
                "lease-1",
                "node-1",
                new Uri("https://router.example"),
                "secret-1"));
        }

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StaticRouterProxyClient(string body) : IRouterProxyClient
    {
        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease,
            string requestJson,
            string requestId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RouterProxyResponse(200, "application/json", body));
        }
    }

    private sealed class CapturingUsageOutboxRepository : IUsageOutboxRepository
    {
        private readonly List<GatewayUsageEvent> enqueued = [];

        public IReadOnlyCollection<GatewayUsageEvent> Enqueued => enqueued;

        public Task EnqueueAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
        {
            enqueued.Add(usageEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<UsageOutboxItem>> LeaseDueAsync(
            int batchSize,
            string workerId,
            TimeSpan leaseDuration,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<UsageOutboxItem>>([]);
        }

        public Task MarkDeliveredAsync(string requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            string requestId,
            int attempts,
            DateTimeOffset nextAttemptAtUtc,
            string lastError,
            bool dead,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
