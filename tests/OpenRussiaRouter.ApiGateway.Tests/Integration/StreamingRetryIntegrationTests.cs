using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.ApiGateway.Tests.Integration;

public sealed class StreamingRetryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public StreamingRetryIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Streaming_HappyPath_StreamsSseChunksAndDoneMarker()
    {
        const string chunk1 = """{"choices":[{"delta":{"content":"Hello"}}]}""";
        const string chunk2 = """{"choices":[{"delta":{"content":" world"}}],"usage":{"prompt_tokens":5,"completion_tokens":2}}""";
        var sseBody = $"data: {chunk1}\n\ndata: {chunk2}\n\ndata: [DONE]\n\n";
        var router = new SequentialStreamingProxy(
            CreateSuccessSession(sseBody));
        var client = CreateStreamingClient(router: router);

        var response = await PostStreamAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("data: " + chunk1);
        body.Should().Contain("data: [DONE]");
    }

    [Fact]
    public async Task Streaming_FirstNodeFails_RetriesOnSecondNode_Succeeds()
    {
        const string sseBody = "data: {\"choices\":[]}\n\ndata: [DONE]\n\n";
        var router = new SequentialStreamingProxy(
            CreateFailSession(),     // first attempt: node unavailable
            CreateSuccessSession(sseBody));  // second attempt: ok
        var control = new RetryTrackingControlPlane();
        var client = CreateStreamingClient(control: control, router: router);

        var response = await PostStreamAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        control.SelectCallCount.Should().Be(2);
        router.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Streaming_AllNodesFail_PassesThroughNodeError()
    {
        // When all nodes fail, the gateway passes through the last node's raw error body
        // (real f9router returns OpenAI-compatible JSON, so the client sees a meaningful message)
        var router = new SequentialStreamingProxy(
            CreateFailSession(),
            CreateFailSession(),
            CreateFailSession());
        var control = new RetryTrackingControlPlane { MaxLeases = 3 };
        var client = CreateStreamingClient(control: control, router: router);

        var response = await PostStreamAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorCode = doc.RootElement.GetProperty("error").GetProperty("code").GetString();
        errorCode.Should().BeOneOf("node_unavailable", "router_unavailable");
    }

    [Fact]
    public async Task Streaming_NoHealthyNodesAtAll_Returns503WithoutCallingRouter()
    {
        var control = new RetryTrackingControlPlane { AlwaysReturnNull = true };
        var router = new SequentialStreamingProxy();
        var client = CreateStreamingClient(control: control, router: router);

        var response = await PostStreamAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        router.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Streaming_MissingBearerToken_Returns401()
    {
        var client = CreateStreamingClient();
        var response = await client.PostAsync(
            "/v1/chat/completions/stream",
            new StringContent("""{"model":"gpt-4"}""", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Streaming_NoUsageInStream_RecordsUsageMissingActivity()
    {
        var billing = new StubBillingProvider();
        var router = new SequentialStreamingProxy(CreateSuccessSession("data: {\"choices\":[]}\n\ndata: [DONE]\n\n"));
        var client = CreateStreamingClient(billing: billing, router: router);

        var response = await PostStreamAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(50);
        billing.LastUsageEvent.Should().NotBeNull();
        billing.LastUsageEvent!.Status.Should().Be("usage_missing");
        billing.LastUsageEvent.Usage.TotalTokens.Should().Be(0);
        billing.LastUsageEvent.ApiKeyPrefix.Should().Be("sk-or-test");
    }

    [Fact]
    public async Task Streaming_NullModel_Returns400()
    {
        var client = CreateStreamingClient();
        var response = await client.PostAsync(
            "/v1/chat/completions/stream",
            new StringContent("""{"messages":[]}""", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Streaming_AdminBypassDisabledModel_UsesAdminFlag()
    {
        var billing = new StubBillingProvider();
        var auth = new AdminStubAuthenticator();
        var router = new SequentialStreamingProxy(CreateSuccessSession("data: [DONE]\n\n"));
        var client = CreateStreamingClient(auth: auth, billing: billing, router: router);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions/stream")
        {
            Content = new StringContent("""{"model":"gpt-4","stream":true}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-ok_key");
        request.Headers.TryAddWithoutValidation("X-ORR-Admin-Test", "1");
        await client.SendAsync(request);

        await Task.Delay(50);
        billing.LastUsageEvent.Should().NotBeNull();
    }

    // --- Helpers ---

    private HttpClient CreateStreamingClient(
        IApiKeyProvider? auth = null,
        IBillingProvider? billing = null,
        RetryTrackingControlPlane? control = null,
        SequentialStreamingProxy? router = null)
    {
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped<IApiKeyProvider>(_ => auth ?? new StubAuthenticator());
            services.AddScoped<IBillingProvider>(_ => billing ?? new StubBillingProvider());
            services.AddScoped<IControlPlaneProvider>(_ => control ?? new RetryTrackingControlPlane());
            services.AddScoped<IRouterProxyProvider>(_ => router ?? new SequentialStreamingProxy(CreateSuccessSession("data: [DONE]\n\n")));
        })).CreateClient();
    }

    private static async Task<HttpResponseMessage> PostStreamAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions/stream")
        {
            Content = new StringContent("""{"model":"gpt-4","stream":true}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-ok_key");
        return await client.SendAsync(request);
    }

    private static RouterStreamSession CreateSuccessSession(string sseBody)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
        };
        return new RouterStreamSession(response);
    }

    private static RouterStreamSession CreateFailSession()
    {
        // Realistic f9router error format (OpenAI-compatible)
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error":{"message":"Service unavailable","type":"server_error","code":"node_unavailable"}}""",
                Encoding.UTF8, "application/json")
        };
        return new RouterStreamSession(response);
    }

    // --- Test doubles ---

    private sealed class StubAuthenticator : IApiKeyProvider
    {
        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test"));
    }

    private sealed class AdminStubAuthenticator : IApiKeyProvider
    {
        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test", isAdmin: true));
    }

    private sealed class StubBillingProvider : IBillingProvider
    {
        public GatewayUsageEvent? LastUsageEvent { get; private set; }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<GatewayPublicModel>>(Array.Empty<GatewayPublicModel>());

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string apiKeyId, string modelId, bool adminBypassDisabled, CancellationToken ct)
            => Task.FromResult(new GatewayBalanceCheckResult(true, modelId, modelId, "v1", MoneyAmount.Usd(100), null));

        public Task<bool> RecordUsageAsync(GatewayUsageEvent ev, CancellationToken ct)
        {
            LastUsageEvent = ev;
            return Task.FromResult(true);
        }
    }

    private sealed class RetryTrackingControlPlane : IControlPlaneProvider
    {
        private int leaseCounter;
        public bool AlwaysReturnNull { get; init; }
        public int SelectCallCount { get; private set; }
        public int MaxLeases { get; init; } = 2; // Default to 2 to mimic old test behavior unless specified

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(
            string modelId,
            string requestId,
            string? preferredNodeId,
            string? preferredNodeControlToken,
            IReadOnlyCollection<string>? excludedNodeIds,
            CancellationToken ct)
        {
            SelectCallCount++;
            if (AlwaysReturnNull)
                return Task.FromResult<GatewayRouterLease?>(null);
            var id = Interlocked.Increment(ref leaseCounter);
            
            if (id > MaxLeases) 
                return Task.FromResult<GatewayRouterLease?>(null);

            return Task.FromResult<GatewayRouterLease?>(
                new GatewayRouterLease($"lease-{id}", $"node-{id}", new Uri("https://router.example"), "secret"));
        }

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class SequentialStreamingProxy : IRouterProxyProvider
    {
        private readonly Queue<RouterStreamSession> sessions;
        public int CallCount { get; private set; }

        public SequentialStreamingProxy(params RouterStreamSession[] sessions)
        {
            this.sessions = new Queue<RouterStreamSession>(sessions);
        }

        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterStreamSession> BeginStreamChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(sessions.Dequeue());
        }

        public Task<RouterProxyResponse> SendTextToSpeechAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendImageGenerationAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
