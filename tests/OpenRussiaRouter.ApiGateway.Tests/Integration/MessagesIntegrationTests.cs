using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Anthropic;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Tests.Integration;

public sealed class MessagesIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MessagesIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateTestClient(
        StubApiKeyProvider? authStub = null,
        StubBillingProvider? billingStub = null,
        StubControlPlaneProvider? cpStub = null,
        StubRouterProxyProvider? proxyStub = null,
        InMemoryUsageOutboxRepository? outboxStub = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SqlMigrations:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                if (authStub != null) services.AddScoped<IApiKeyProvider>(_ => authStub);
                if (billingStub != null) services.AddScoped<IBillingProvider>(_ => billingStub);
                if (cpStub != null) services.AddScoped<IControlPlaneProvider>(_ => cpStub);
                if (proxyStub != null) services.AddScoped<IRouterProxyProvider>(_ => proxyStub);
                if (outboxStub != null) services.AddScoped<IUsageOutboxRepository>(_ => outboxStub);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task PostMessages_BothHeadersMatch_SuccessAsync()
    {
        var authStub = new StubApiKeyProvider();
        authStub.Set("sk-or-valid", "user-1", "key-1", "sk-or-va");

        var billingStub = new StubBillingProvider();
        billingStub.Set("user-1", "key-1", "claude-3-7-sonnet", new GatewayBalanceCheckResult(
            Allowed: true,
            ModelId: "claude-3-7-sonnet",
            RouterModelId: "claude-3-7-sonnet-20250219",
            PriceVersion: "v1",
            Balance: OpenRussiaRouter.BuildingBlocks.Money.MoneyAmount.Usd(100),
            ErrorCode: null,
            SupportsReasoning: true,
            AllowedReasoningEfforts: ["low", "medium", "high"],
            DefaultReasoningEffort: "medium"));

        var cpStub = new StubControlPlaneProvider();
        cpStub.Set("claude-3-7-sonnet-20250219", new GatewayRouterLease("lease-1", "node-1", new Uri("http://node-1.local"), "tok"));

        var proxyStub = new StubRouterProxyProvider();
        proxyStub.MessagesResponse = new RouterProxyResponse(200, "application/json", """
        {
            "id": "msg_01",
            "type": "message",
            "role": "assistant",
            "content": [{"type": "text", "text": "Hello Anthropic"}],
            "model": "claude-3-7-sonnet-20250219",
            "usage": {"input_tokens": 10, "output_tokens": 5}
        }
        """, "anthropic", "acc1");

        var outboxStub = new InMemoryUsageOutboxRepository();
        var client = CreateTestClient(authStub, billingStub, cpStub, proxyStub, outboxStub);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sk-or-valid");
        request.Headers.Add("x-api-key", "sk-or-valid");
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent("""{"model":"claude-3-7-sonnet","messages":[{"role":"user","content":"Hi"}]}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Hello Anthropic");

        var records = outboxStub.GetAll();
        records.Should().HaveCount(1);
        var ev = records.First();
        ev.Endpoint.Should().Be("messages");
        ev.Stream.Should().BeFalse();
        ev.Usage.InputTokens.Should().Be(10);
        ev.Usage.OutputTokens.Should().Be(5);
    }

    [Fact]
    public async Task PostMessages_ConflictingAuthHeaders_ReturnsAnthropicErrorAsync()
    {
        var client = CreateTestClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sk-or-key1");
        request.Headers.Add("x-api-key", "sk-or-key2");
        request.Content = new StringContent("""{"model":"claude-3-7-sonnet","messages":[{"role":"user","content":"Hi"}]}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"error\"");
        body.Should().Contain("\"type\":\"authentication_error\"");
    }

    [Fact]
    public async Task PostMessages_Streaming_SuccessAsync()
    {
        var authStub = new StubApiKeyProvider();
        authStub.Set("sk-or-valid", "user-1", "key-1", "sk-or-va");

        var billingStub = new StubBillingProvider();
        billingStub.Set("user-1", "key-1", "claude-3-7-sonnet", new GatewayBalanceCheckResult(
            Allowed: true,
            ModelId: "claude-3-7-sonnet",
            RouterModelId: "claude-3-7-sonnet-20250219",
            PriceVersion: "v1",
            Balance: OpenRussiaRouter.BuildingBlocks.Money.MoneyAmount.Usd(100),
            ErrorCode: null,
            SupportsReasoning: true,
            AllowedReasoningEfforts: ["low", "medium", "high"],
            DefaultReasoningEffort: "medium"));

        var cpStub = new StubControlPlaneProvider();
        cpStub.Set("claude-3-7-sonnet-20250219", new GatewayRouterLease("lease-1", "node-1", new Uri("http://node-1.local"), "tok"));

        var proxyStub = new StubRouterProxyProvider();
        proxyStub.StreamHandler = () =>
        {
            var sse = """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-3-7-sonnet-20250219","usage":{"input_tokens":100,"output_tokens":0,"cache_read_input_tokens":20}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Streaming test"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":30}}

            event: message_stop
            data: {"type":"message_stop"}

            """;
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
            return new AnthropicStreamSession(msg);
        };

        var outboxStub = new InMemoryUsageOutboxRepository();
        var client = CreateTestClient(authStub, billingStub, cpStub, proxyStub, outboxStub);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages?beta=true");
        request.Headers.Add("x-api-key", "sk-or-valid");
        request.Content = new StringContent("""{"model":"claude-3-7-sonnet","stream":true,"messages":[{"role":"user","content":"Hi"}]}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Streaming test");

        var records = outboxStub.GetAll();
        records.Should().HaveCount(1);
        var ev = records.First();
        ev.Endpoint.Should().Be("messages");
        ev.Stream.Should().BeTrue();
        ev.Usage.InputTokens.Should().Be(120); // 100 + 20
        ev.Usage.OutputTokens.Should().Be(30);
        ev.Usage.CacheTokens.Should().Be(20);
    }

    private sealed class StubApiKeyProvider : IApiKeyProvider
    {
        private readonly Dictionary<string, (string UserId, string ApiKeyId, string Prefix)> _keys = new();

        public void Set(string key, string userId, string apiKeyId, string prefix)
        {
            _keys[key] = (userId, apiKeyId, prefix);
        }

        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (_keys.TryGetValue(apiKey, out var data))
            {
                return Task.FromResult(ApiKeyAuthenticationResult.Success(data.UserId, data.ApiKeyId, data.Prefix));
            }
            return Task.FromResult(ApiKeyAuthenticationResult.Failed(ErrorCodes.AuthenticationRequired));
        }
    }

    private sealed class StubBillingProvider : IBillingProvider
    {
        private readonly Dictionary<string, GatewayBalanceCheckResult> _results = new();

        public void Set(string userId, string apiKeyId, string modelId, GatewayBalanceCheckResult result)
        {
            _results[$"{userId}:{apiKeyId}:{modelId}"] = result;
        }

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string apiKeyId, string modelId, bool adminBypassDisabled, CancellationToken cancellationToken)
        {
            if (_results.TryGetValue($"{userId}:{apiKeyId}:{modelId}", out var res))
                return Task.FromResult(res);

            return Task.FromResult(new GatewayBalanceCheckResult(false, modelId, modelId, null, OpenRussiaRouter.BuildingBlocks.Money.MoneyAmount.Usd(0), ErrorCodes.ModelUnavailable, false, null, null));
        }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<GatewayPublicModel>>([]);

        public Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class StubControlPlaneProvider : IControlPlaneProvider
    {
        private readonly Dictionary<string, GatewayRouterLease> _leases = new();

        public void Set(string routerModelId, GatewayRouterLease lease)
        {
            _leases[routerModelId] = lease;
        }

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(string routerModelId, string requestId, string? preferredNodeId, string? preferredNodeControlToken, IReadOnlyCollection<string>? excludedNodeIds, CancellationToken cancellationToken)
        {
            if (_leases.TryGetValue(routerModelId, out var lease))
                return Task.FromResult<GatewayRouterLease?>(lease);

            return Task.FromResult<GatewayRouterLease?>(null);
        }

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubRouterProxyProvider : IRouterProxyProvider
    {
        public RouterProxyResponse? MessagesResponse { get; set; }
        public Func<AnthropicStreamSession>? StreamHandler { get; set; }

        public Task<RouterProxyResponse> SendChatCompletionAsync(GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<RouterStreamSession> BeginStreamChatCompletionAsync(GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<RouterProxyResponse> SendImageGenerationAsync(GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<RouterProxyResponse> SendTextToSpeechAsync(GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<RouterProxyResponse> SendMessagesAsync(GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, IHeaderDictionary? clientHeaders, string? queryString, CancellationToken cancellationToken)
        {
            return Task.FromResult(MessagesResponse ?? new RouterProxyResponse(200, "application/json", "{}", null, null));
        }

        public Task<AnthropicStreamSession> BeginStreamMessagesAsync(GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, IHeaderDictionary? clientHeaders, string? queryString, CancellationToken cancellationToken)
        {
            if (StreamHandler != null)
                return Task.FromResult(StreamHandler());

            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(new AnthropicStreamSession(msg));
        }
    }
}
