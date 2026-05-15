using System.Net;
using System.Text;
using System.Text.Json;
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: " + chunk1, body);
        Assert.Contains("data: [DONE]", body);
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, control.SelectCallCount);
        Assert.Equal(2, router.CallCount);
    }

    [Fact]
    public async Task Streaming_BothNodesFail_Returns503()
    {
        var router = new SequentialStreamingProxy(
            CreateFailSession(),
            CreateFailSession());
        var client = CreateStreamingClient(router: router);

        var response = await PostStreamAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.RouterUnavailable, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Streaming_NoHealthyNodesAtAll_Returns503WithoutCallingRouter()
    {
        var control = new RetryTrackingControlPlane { AlwaysReturnNull = true };
        var router = new SequentialStreamingProxy();
        var client = CreateStreamingClient(control: control, router: router);

        var response = await PostStreamAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, router.CallCount);
    }

    [Fact]
    public async Task Streaming_MissingBearerToken_Returns401()
    {
        var client = CreateStreamingClient();
        var response = await client.PostAsync(
            "/v1/chat/completions/stream",
            new StringContent("""{"model":"gpt-4"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

    private HttpClient CreateStreamingClient(
        IApiKeyAuthenticator? auth = null,
        IBillingGatewayClient? billing = null,
        RetryTrackingControlPlane? control = null,
        SequentialStreamingProxy? router = null)
    {
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped<IApiKeyAuthenticator>(_ => auth ?? new StubAuthenticator());
            services.AddScoped<IBillingGatewayClient>(_ => billing ?? new StubBillingClient());
            services.AddScoped<IControlPlaneGatewayClient>(_ => control ?? new RetryTrackingControlPlane());
            services.AddScoped<IRouterProxyClient>(_ => router ?? new SequentialStreamingProxy(CreateSuccessSession("data: [DONE]\n\n")));
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
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error":"node down"}""", Encoding.UTF8, "application/json")
        };
        return new RouterStreamSession(response);
    }

    // --- Test doubles ---

    private sealed class StubAuthenticator : IApiKeyAuthenticator
    {
        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(ApiKeyAuthenticationResult.Success("user-1", "key-1"));
    }

    private sealed class StubBillingClient : IBillingGatewayClient
    {
        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<GatewayPublicModel>>(Array.Empty<GatewayPublicModel>());

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, CancellationToken ct)
            => Task.FromResult(new GatewayBalanceCheckResult(true, modelId, "v1", MoneyAmount.Rub(100), null));

        public Task<bool> RecordUsageAsync(GatewayUsageEvent ev, CancellationToken ct)
            => Task.FromResult(true);
    }

    private sealed class RetryTrackingControlPlane : IControlPlaneGatewayClient
    {
        private int leaseCounter;
        public bool AlwaysReturnNull { get; init; }
        public int SelectCallCount { get; private set; }

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(string modelId, string requestId, CancellationToken ct)
        {
            SelectCallCount++;
            if (AlwaysReturnNull)
                return Task.FromResult<GatewayRouterLease?>(null);
            var id = Interlocked.Increment(ref leaseCounter);
            return Task.FromResult<GatewayRouterLease?>(
                new GatewayRouterLease($"lease-{id}", $"node-{id}", new Uri("https://router.example"), "secret"));
        }

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class SequentialStreamingProxy : IRouterProxyClient
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
    }
}
