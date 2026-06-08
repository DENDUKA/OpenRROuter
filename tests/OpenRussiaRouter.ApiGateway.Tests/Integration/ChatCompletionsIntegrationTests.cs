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

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCode(response, ErrorCodes.AuthenticationRequired);
    }

    [Fact]
    public async Task NonStreaming_InvalidApiKey_Returns401()
    {
        var auth = new StubAuthenticator { Result = ApiKeyAuthenticationResult.Failed(ErrorCodes.InvalidApiKey) };
        var client = CreateClient(auth);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-invalid_key");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCode(response, ErrorCodes.InvalidApiKey);
    }

    // --- Billing ---

    [Fact]
    public async Task NonStreaming_InsufficientBalance_Returns402()
    {
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(false, "gpt-4", "gpt-4", null, MoneyAmount.Usd(0), ErrorCodes.InsufficientBalance)
        };
        var client = CreateClient(new StubAuthenticator(), billing);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        await AssertErrorCode(response, ErrorCodes.InsufficientBalance);
    }

    [Fact]
    public async Task NonStreaming_UnknownModel_ReturnsBillingError()
    {
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(false, "unknown-model", "unknown-model", null, MoneyAmount.Usd(100), ErrorCodes.ValidationFailed)
        };
        var client = CreateClient(new StubAuthenticator(), billing);

        var response = await PostChatAsync(client, """{"model":"unknown-model"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorCode(response, ErrorCodes.ValidationFailed);
    }

    // --- Control Plane ---

    [Fact]
    public async Task NonStreaming_NoHealthyNodes_Returns503()
    {
        var control = new StubControlPlane { Lease = null };
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), control);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertErrorCode(response, ErrorCodes.RouterUnavailable);
    }

    [Fact]
    public async Task NonStreaming_PreferredNodeHeaders_ArePassedToControlPlane()
    {
        var control = new StubControlPlane();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), control);

        var response = await PostChatAsync(
            client,
            """{"model":"gpt-4"}""",
            bearer: "sk-ok_key",
            preferredNodeId: "node-preferred",
            preferredNodeControlToken: "node-secret");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        control.LastPreferredNodeId.Should().Be("node-preferred");
        control.LastPreferredNodeControlToken.Should().Be("node-secret");
    }

    // --- Happy Path ---

    [Fact]
    public async Task NonStreaming_HappyPath_Returns200WithRouterBody()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[{"message":{"content":"Hi"}}],"usage":{"prompt_tokens":5,"completion_tokens":3}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(routerBody);
    }

    [Fact]
    public async Task NonStreaming_GeminiModel_SanitizesJsonSchemaRefsBeforeRouter()
    {
        var router = new StubRouterProxy();
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(
                true,
                "gemini",
                "gemini-cli/gemini-3.1-pro-preview",
                "v1",
                MoneyAmount.Usd(100),
                null)
        };
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        const string requestBody = """
        {
          "model": "gemini",
          "tools": [
            {
              "type": "function",
              "function": {
                "name": "ask",
                "parameters": {
                  "type": "object",
                  "properties": {
                    "question": {
                      "$ref": "#/$defs/QuestionPrompt",
                      "description": "breaks Gemini schema conversion"
                    }
                  },
                  "$defs": {
                    "QuestionPrompt": {
                      "type": "object",
                      "properties": {
                        "text": { "type": "string" }
                      }
                    }
                  }
                }
              }
            }
          ]
        }
        """;

        var response = await PostChatAsync(client, requestBody, bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        router.LastRequestJson.Should().NotBeNull();
        using var document = JsonDocument.Parse(router.LastRequestJson!);
        document.RootElement.GetProperty("model").GetString().Should().Be("gemini-cli/gemini-3.1-pro-preview");
        var questionSchema = document.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("parameters")
            .GetProperty("properties")
            .GetProperty("question");
        questionSchema.TryGetProperty("$ref", out _).Should().BeFalse();
        questionSchema.GetProperty("type").GetString().Should().Be("object");
        questionSchema.GetProperty("properties").GetProperty("text").GetProperty("type").GetString()
            .Should().Be("string");
    }

    [Fact]
    public async Task NonStreaming_RouterUpstream5xx_ReleasesLeaseAsFailed()
    {
        var router = new StubRouterProxy { Response = new RouterProxyResponse(500, "application/json", """{"error":"upstream"}""") };
        var control = new StubControlPlane();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), control, router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        control.LastReleasedStatus.Should().Be("failed");
    }

    [Fact]
    public async Task NonStreaming_UnsupportedReasoning_Returns400WithoutCallingRouter()
    {
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(
                true,
                "gpt-4",
                "gpt-4",
                "v1",
                MoneyAmount.Usd(100),
                null,
                SupportsReasoning: false)
        };
        var router = new StubRouterProxy();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4","reasoning_effort":"high"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorCode(response, ErrorCodes.UnsupportedReasoning);
        router.LastRequestJson.Should().BeNull();
    }

    [Fact]
    public async Task NonStreaming_HappyPath_RecordsUsageAfterSuccessfulResponse()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingProvider();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        // Fire-and-forget usage recording — give it a moment to complete
        await Task.Delay(50);
        billing.RecordUsageCalled.Should().BeTrue();
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventContainsApiKeyPrefix()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingProvider();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        await Task.Delay(50);
        billing.LastUsageEvent.Should().NotBeNull();
        billing.LastUsageEvent!.ApiKeyPrefix.Should().Be("sk-or-test");
    }

    [Fact]
    public async Task NonStreaming_RouterBodyWithoutUsage_RecordsUsageMissingActivity()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[{"message":{"content":"Hi"}}]}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingProvider();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        await Task.Delay(50);
        billing.LastUsageEvent.Should().NotBeNull();
        billing.LastUsageEvent!.Status.Should().Be("usage_missing");
        billing.LastUsageEvent.Usage.TotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventDoesNotContainRequestBody()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":7,"completion_tokens":4}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingProvider();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        await PostChatAsync(client, """{"model":"gpt-4","messages":[{"role":"user","content":"secret prompt"}]}""", bearer: "sk-ok_key");

        await Task.Delay(50);
        billing.RecordUsageCalled.Should().BeTrue();
        billing.LastUsageEvent.Should().NotBeNull();
        // E2E-004: prompt bodies must never appear in usage events
        JsonSerializer.Serialize(billing.LastUsageEvent).Should().NotContain("secret prompt");
    }

    // --- Models endpoint ---

    [Fact]
    public async Task ListModels_MissingBearerToken_Returns401()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/v1/models");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListModels_ValidToken_Returns200WithObjectList()
    {
        var billing = new StubBillingProvider
        {
            PublicModels = new[] { new GatewayPublicModel("gpt-4", "openai", true, ["low", "high"]) }
        };
        var client = CreateClient(billing: billing);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-ok_key");

        var response = await client.GetAsync("/v1/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("object").GetString().Should().Be("list");
        json.RootElement.GetProperty("data")[0].GetProperty("capabilities").GetProperty("reasoning").GetProperty("supported").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("data")[0].GetProperty("capabilities").GetProperty("reasoning").GetProperty("efforts")[0].GetString().Should().Be("low");
    }

    // --- Helpers ---

    private HttpClient CreateClient(
        IApiKeyProvider? auth = null,
        IBillingProvider? billing = null,
        IControlPlaneProvider? control = null,
        IRouterProxyProvider? router = null)
    {
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped<IApiKeyProvider>(_ => auth ?? new StubAuthenticator());
            services.AddScoped<IBillingProvider>(_ => billing ?? new StubBillingProvider());
            services.AddScoped<IControlPlaneProvider>(_ => control ?? new StubControlPlane());
            services.AddScoped<IRouterProxyProvider>(_ => router ?? new StubRouterProxy());
        })).CreateClient();
    }

    private static async Task<HttpResponseMessage> PostChatAsync(
        HttpClient client,
        string body,
        string? bearer = null,
        string? preferredNodeId = null,
        string? preferredNodeControlToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (preferredNodeId is not null)
            request.Headers.TryAddWithoutValidation("X-ORR-Preferred-Node-Id", preferredNodeId);
        if (preferredNodeControlToken is not null)
            request.Headers.TryAddWithoutValidation("X-ORR-Preferred-Node-Token", preferredNodeControlToken);
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
    }

    // --- Test doubles ---

    private sealed class StubAuthenticator : IApiKeyProvider
    {
        public ApiKeyAuthenticationResult Result { get; set; } = ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test");

        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(Result);
    }

    private sealed class StubBillingProvider : IBillingProvider
    {
        public GatewayBalanceCheckResult CheckResult { get; set; } =
            new(true, "gpt-4", "gpt-4", "v1", MoneyAmount.Usd(100), null);

        public IReadOnlyCollection<GatewayPublicModel>? PublicModels { get; set; }
        public bool RecordUsageCalled { get; private set; }
        public GatewayUsageEvent? LastUsageEvent { get; private set; }

        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken ct)
            => Task.FromResult(PublicModels ?? (IReadOnlyCollection<GatewayPublicModel>)Array.Empty<GatewayPublicModel>());

        public bool LastAdminBypassDisabled { get; private set; }

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string modelId, bool adminBypassDisabled, CancellationToken ct)
        {
            LastAdminBypassDisabled = adminBypassDisabled;
            return Task.FromResult(CheckResult);
        }

        public Task<bool> RecordUsageAsync(GatewayUsageEvent ev, CancellationToken ct)
        {
            RecordUsageCalled = true;
            LastUsageEvent = ev;
            return Task.FromResult(true);
        }
    }

    private sealed class StubControlPlane : IControlPlaneProvider
    {
        public GatewayRouterLease? Lease { get; set; } =
            new("lease-1", "node-1", new Uri("https://router.example"), "secret");

        public string? LastReleasedStatus { get; private set; }
        public string? LastPreferredNodeId { get; private set; }
        public string? LastPreferredNodeControlToken { get; private set; }

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(
            string modelId,
            string requestId,
            string? preferredNodeId,
            string? preferredNodeControlToken,
            IReadOnlyCollection<string>? excludedNodeIds,
            CancellationToken ct)
        {
            LastPreferredNodeId = preferredNodeId;
            LastPreferredNodeControlToken = preferredNodeControlToken;
            return Task.FromResult(Lease);
        }

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken ct)
        {
            LastReleasedStatus = status;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRouterProxy : IRouterProxyProvider
    {
        public RouterProxyResponse Response { get; set; } =
            new(200, "application/json", """{"id":"chatcmpl-1","choices":[]}""");

        public string? LastRequestJson { get; private set; }

        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
        {
            LastRequestJson = requestJson;
            return Task.FromResult(Response);
        }

        public Task<RouterStreamSession> BeginStreamChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException("Use StreamingStubRouterProxy for streaming tests.");
    }
}
