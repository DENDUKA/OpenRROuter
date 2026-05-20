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
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Tests.Integration;

public sealed class ChatCompletionsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ChatCompletionsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    // --- Authentication ---

    [Fact]
    public async Task NonStreaming_MissingBearerToken_Returns401()
    {
        var client = CreateClient(new StubAuthenticator());

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCode(response, ErrorCodes.AuthenticationRequired);
    }

    [Fact]
    public async Task NonStreaming_InvalidApiKey_Returns401()
    {
        var auth = new StubAuthenticator { Result = ApiKeyAuthenticationResult.Failed(ErrorCodes.InvalidApiKey) };
        var client = CreateClient(auth);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-invalid_key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCode(response, ErrorCodes.InvalidApiKey);
    }

    // --- Billing ---

    [Fact]
    public async Task NonStreaming_InsufficientBalance_Returns402()
    {
        var billing = new StubBillingClient
        {
            CheckResult = new GatewayBalanceCheckResult(false, "gpt-4", null, MoneyAmount.Rub(0), ErrorCodes.InsufficientBalance)
        };
        var client = CreateClient(new StubAuthenticator(), billing);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        await AssertErrorCode(response, ErrorCodes.InsufficientBalance);
    }

    [Fact]
    public async Task NonStreaming_UnknownModel_ReturnsBillingError()
    {
        var billing = new StubBillingClient
        {
            CheckResult = new GatewayBalanceCheckResult(false, "unknown-model", null, MoneyAmount.Rub(100), ErrorCodes.ValidationFailed)
        };
        var client = CreateClient(new StubAuthenticator(), billing);

        var response = await PostChatAsync(client, """{"model":"unknown-model"}""", bearer: "sk-ok_key");

        // billing_error type always maps to 402 in WriteJsonErrorAsync
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        await AssertErrorCode(response, ErrorCodes.ValidationFailed);
    }

    // --- Control Plane ---

    [Fact]
    public async Task NonStreaming_NoHealthyNodes_Returns503()
    {
        var control = new StubControlPlane { Lease = null };
        var client = CreateClient(new StubAuthenticator(), new StubBillingClient(), control);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertErrorCode(response, ErrorCodes.RouterUnavailable);
    }

    // --- Happy Path ---

    [Fact]
    public async Task NonStreaming_HappyPath_Returns200WithRouterBody()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[{"message":{"content":"Hi"}}],"usage":{"prompt_tokens":5,"completion_tokens":3}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var client = CreateClient(new StubAuthenticator(), new StubBillingClient(), new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(routerBody, body);
    }

    [Fact]
    public async Task NonStreaming_RouterUpstream5xx_ReleasesLeaseAsFailed()
    {
        var router = new StubRouterProxy { Response = new RouterProxyResponse(500, "application/json", """{"error":"upstream"}""") };
        var control = new StubControlPlane();
        var client = CreateClient(new StubAuthenticator(), new StubBillingClient(), control, router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        Assert.Equal("failed", control.LastReleasedStatus);
    }

    [Fact]
    public async Task NonStreaming_HappyPath_RecordsUsageAfterSuccessfulResponse()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingClient();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        // Fire-and-forget usage recording — give it a moment to complete
        await Task.Delay(50);
        Assert.True(billing.RecordUsageCalled);
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventContainsApiKeyPrefix()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingClient();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        await Task.Delay(50);
        Assert.NotNull(billing.LastUsageEvent);
        Assert.Equal("sk-or-test", billing.LastUsageEvent.ApiKeyPrefix);
    }

    [Fact]
    public async Task NonStreaming_RouterBodyWithoutUsage_RecordsUsageMissingActivity()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[{"message":{"content":"Hi"}}]}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingClient();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        await Task.Delay(50);
        Assert.NotNull(billing.LastUsageEvent);
        Assert.Equal("usage_missing", billing.LastUsageEvent.Status);
        Assert.Equal(0, billing.LastUsageEvent.Usage.TotalTokens);
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventDoesNotContainRequestBody()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":7,"completion_tokens":4}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingClient();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4","messages":[{"role":"user","content":"secret prompt"}]}""", bearer: "sk-ok_key");

        await Task.Delay(50);
        Assert.True(billing.RecordUsageCalled);
        Assert.NotNull(billing.LastUsageEvent);
        // E2E-004: prompt bodies must never appear in usage events
        Assert.DoesNotContain("secret prompt", JsonSerializer.Serialize(billing.LastUsageEvent));
    }

    // --- Models endpoint ---

    [Fact]
    public async Task ListModels_MissingBearerToken_Returns401()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListModels_ValidToken_Returns200WithObjectList()
    {
        var billing = new StubBillingClient
        {
            PublicModels = new[] { new GatewayPublicModel("gpt-4", "openai") }
        };
        var client = CreateClient(billing: billing);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-ok_key");

        var response = await client.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("list", json.RootElement.GetProperty("object").GetString());
    }

    // --- Helpers ---

    private HttpClient CreateClient(
        IApiKeyAuthenticator? auth = null,
        IBillingGatewayClient? billing = null,
        IControlPlaneGatewayClient? control = null,
        IRouterProxyClient? router = null)
    {
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped<IApiKeyAuthenticator>(_ => auth ?? new StubAuthenticator());
            services.AddScoped<IBillingGatewayClient>(_ => billing ?? new StubBillingClient());
            services.AddScoped<IControlPlaneGatewayClient>(_ => control ?? new StubControlPlane());
            services.AddScoped<IRouterProxyClient>(_ => router ?? new StubRouterProxy());
        })).CreateClient();
    }

    private static async Task<HttpResponseMessage> PostChatAsync(
        HttpClient client, string body, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    // --- Test doubles ---

    private sealed class StubAuthenticator : IApiKeyAuthenticator
    {
        public ApiKeyAuthenticationResult Result { get; set; } = ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test");

        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(Result);
    }

    private sealed class StubBillingClient : IBillingGatewayClient
    {
        public GatewayBalanceCheckResult CheckResult { get; set; } =
            new(true, "gpt-4", "v1", MoneyAmount.Rub(100), null);

        public IReadOnlyCollection<GatewayPublicModel>? PublicModels { get; set; }
        public bool RecordUsageCalled { get; private set; }
        public GatewayUsageEvent? LastUsageEvent { get; private set; }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken ct)
            => Task.FromResult(PublicModels ?? (IReadOnlyCollection<GatewayPublicModel>)Array.Empty<GatewayPublicModel>());

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, CancellationToken ct)
            => Task.FromResult(CheckResult);

        public Task<bool> RecordUsageAsync(GatewayUsageEvent ev, CancellationToken ct)
        {
            RecordUsageCalled = true;
            LastUsageEvent = ev;
            return Task.FromResult(true);
        }
    }

    private sealed class StubControlPlane : IControlPlaneGatewayClient
    {
        public GatewayRouterLease? Lease { get; set; } =
            new("lease-1", "node-1", new Uri("https://router.example"), "secret");

        public string? LastReleasedStatus { get; private set; }

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(string modelId, string requestId, CancellationToken ct)
            => Task.FromResult(Lease);

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken ct)
        {
            LastReleasedStatus = status;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRouterProxy : IRouterProxyClient
    {
        public RouterProxyResponse Response { get; set; } =
            new(200, "application/json", """{"id":"chatcmpl-1","choices":[]}""");

        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => Task.FromResult(Response);

        public Task<RouterStreamSession> BeginStreamChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException("Use StreamingStubRouterProxy for streaming tests.");
    }
}
