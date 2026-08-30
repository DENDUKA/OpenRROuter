using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public async Task NonStreaming_ApiKeyLimitExceeded_Returns402AndDoesNotCallRouter()
    {
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(false, "gpt-4", "gpt-4", null, MoneyAmount.Usd(100), ErrorCodes.ApiKeyLimitExceeded)
        };
        var router = new StubRouterProxy();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        await AssertErrorCode(response, ErrorCodes.ApiKeyLimitExceeded);
        router.LastRequestJson.Should().BeNull();
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
    public async Task NonStreaming_UnsupportedReasoning_IsStrippedAndForwarded()
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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        router.LastRequestJson.Should().NotBeNull();
        using var document = JsonDocument.Parse(router.LastRequestJson!);
        document.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Fact]
    public async Task NonStreaming_UnsupportedReasoningEffort_FallsBackToFirstAllowed()
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
                SupportsReasoning: true,
                AllowedReasoningEfforts: ["low", "medium"])
        };
        var router = new StubRouterProxy();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4","reasoning_effort":"high"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        router.LastRequestJson.Should().NotBeNull();
        using var document = JsonDocument.Parse(router.LastRequestJson!);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("low");
    }

    [Fact]
    public async Task NonStreaming_InvalidReasoningEffort_FallsBackToDefaultWhenConfigured()
    {
        // Admin set DefaultReasoningEffort="low" → invalid client effort "high" must be
        // silently replaced with "low" and the request forwarded to the node.
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(
                true,
                "gpt-4",
                "gpt-4",
                "v1",
                MoneyAmount.Usd(100),
                null,
                SupportsReasoning: true,
                AllowedReasoningEfforts: ["low", "medium"],
                DefaultReasoningEffort: "low")
        };
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4","reasoning_effort":"high"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        router.LastRequestJson.Should().NotBeNull();
        using var document = JsonDocument.Parse(router.LastRequestJson!);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("low");
    }

    [Fact]
    public async Task NonStreaming_NoReasoningInRequest_AppliesModelDefaultEffort()
    {
        // Plain request with no reasoning fields → gateway must inject admin-configured default.
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(
                true,
                "gpt-4",
                "gpt-4",
                "v1",
                MoneyAmount.Usd(100),
                null,
                SupportsReasoning: true,
                AllowedReasoningEfforts: ["low", "medium", "high"],
                DefaultReasoningEffort: "high")
        };
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        router.LastRequestJson.Should().NotBeNull();
        using var document = JsonDocument.Parse(router.LastRequestJson!);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("high");
    }

    [Fact]
    public async Task NonStreaming_HappyPath_EnqueuesUsageRecord()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var outbox = new InMemoryUsageOutboxRepository();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), new StubControlPlane(), router, outbox);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        outbox.GetAll().Should().ContainSingle();
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventContainsApiKeyPrefix()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var outbox = new InMemoryUsageOutboxRepository();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), new StubControlPlane(), router, outbox);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        var record = outbox.GetAll().Single();
        record.ApiKeyPrefix.Should().Be("sk-or-test");
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventCarriesChatCompletionsEndpointAndStreamFalse()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var outbox = new InMemoryUsageOutboxRepository();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), new StubControlPlane(), router, outbox);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        var record = outbox.GetAll().Single();
        record.Endpoint.Should().Be("chat_completions");
        record.Stream.Should().BeFalse();
    }

    [Fact]
    public async Task NonStreaming_RouterBodyWithoutUsage_RecordsUsageMissingActivity()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[{"message":{"content":"Hi"}}]}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var outbox = new InMemoryUsageOutboxRepository();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), new StubControlPlane(), router, outbox);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        var record = outbox.GetAll().Single();
        record.Status.Should().Be("usage_missing");
        record.Usage.TotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task NonStreaming_HappyPath_UsageEventDoesNotContainRequestBody()
    {
        const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":7,"completion_tokens":4}}""";
        var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
        var outbox = new InMemoryUsageOutboxRepository();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), new StubControlPlane(), router, outbox);

        await PostChatAsync(client, """{"model":"gpt-4","messages":[{"role":"user","content":"secret prompt"}]}""", bearer: "sk-ok_key");

        var record = outbox.GetAll().Single();
        // E2E-004: prompt bodies must never appear in usage events
        JsonSerializer.Serialize(record.ToGatewayUsageEvent()).Should().NotContain("secret prompt");
    }

    // --- Crawler signals ---

    [Fact]
    public async Task RobotsTxt_DisallowsAllPaths()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/robots.txt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Be("User-agent: *\nDisallow: /\n");
    }

    [Fact]
    public async Task PublicApiResponses_IncludeNoIndexHeader()
    {
        var client = CreateClient();

        var modelsResponse = await client.GetAsync("/v1/models");
        var healthResponse = await client.GetAsync("/health");

        modelsResponse.Headers.GetValues("X-Robots-Tag").Single().Should().Be("noindex, nofollow");
        healthResponse.Headers.GetValues("X-Robots-Tag").Single().Should().Be("noindex, nofollow");
    }

    // --- Models endpoint ---

    [Fact]
    public async Task ListModels_IsPublic_Returns200WithoutBearerToken()
    {
        // /v1/models is intentionally public to align with the OpenRouter contract —
        // clients (opencode plugin, Cline, …) probe it for capability discovery before
        // they have a chance to send credentials.
        var client = CreateClient();
        var response = await client.GetAsync("/v1/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListModels_ReasoningModel_AdvertisesOpenRouterContract()
    {
        var billing = new StubBillingProvider
        {
            PublicModels = new[]
            {
                new GatewayPublicModel(
                    "ORR/GPT-5.5",
                    "GPT-5.5",
                    SupportsReasoning: true,
                    AllowedReasoningEfforts: ["low", "medium", "high"],
                    DefaultReasoningEffort: "medium"),
                new GatewayPublicModel("ORR/gpt-4o", "GPT-4o")
            }
        };
        var client = CreateClient(new StubAuthenticator(), billing);

        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-test");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        data.Should().HaveCount(2);

        var reasoning = data.Single(e => e.GetProperty("id").GetString() == "ORR/GPT-5.5");
        // OpenRouter-style top-level `reasoning` object present with the canonical fields.
        reasoning.GetProperty("reasoning").GetProperty("mandatory").GetBoolean().Should().BeFalse();
        reasoning.GetProperty("reasoning").GetProperty("supported_efforts")
            .EnumerateArray().Select(t => t.GetString()).Should().Equal("low", "medium", "high");
        reasoning.GetProperty("reasoning").GetProperty("default_effort").GetString().Should().Be("medium");
        // supported_parameters contains the paired `reasoning` + `include_reasoning` strings.
        var parameters = reasoning.GetProperty("supported_parameters")
            .EnumerateArray().Select(t => t.GetString()).ToList();
        parameters.Should().Contain("reasoning");
        parameters.Should().Contain("include_reasoning");
        parameters.Should().BeInAscendingOrder(StringComparer.Ordinal);

        // Non-reasoning model: top-level `reasoning` field must be entirely absent (not null).
        var plain = data.Single(e => e.GetProperty("id").GetString() == "ORR/gpt-4o");
        plain.TryGetProperty("reasoning", out _).Should().BeFalse();
        plain.GetProperty("supported_parameters")
            .EnumerateArray().Select(t => t.GetString()).Should().NotContain("reasoning");
    }

    // --- Validation ---

    [Fact]
    public async Task NonStreaming_NullModel_Returns400()
    {
        var client = CreateClient(new StubAuthenticator());

        var response = await PostChatAsync(client, """{"messages":[]}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorCode(response, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task NonStreaming_EmptyModel_Returns400()
    {
        var client = CreateClient(new StubAuthenticator());

        var response = await PostChatAsync(client, """{"model":"","messages":[]}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorCode(response, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task NonStreaming_ModelUnavailable_Returns400WithCorrectType()
    {
        var billing = new StubBillingProvider
        {
            CheckResult = new GatewayBalanceCheckResult(false, "gpt-4", "gpt-4", null, MoneyAmount.Usd(100), ErrorCodes.ModelUnavailable)
        };
        var client = CreateClient(new StubAuthenticator(), billing);

        var response = await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorCode(response, ErrorCodes.ModelUnavailable);
    }

    [Fact]
    public async Task NonStreaming_AdminBypassDisabledModel_UsesAdminFlag()
    {
        var billing = new StubBillingProvider();
        var auth = new StubAuthenticator
        {
            Result = ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test", isAdmin: true)
        };
        var client = CreateClient(auth, billing);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent("""{"model":"gpt-4"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-ok_key");
        request.Headers.TryAddWithoutValidation("X-ORR-Admin-Test", "1");
        await client.SendAsync(request);

        billing.LastAdminBypassDisabled.Should().BeTrue();
    }

    [Fact]
    public async Task NonStreaming_BillingPrecheck_ReceivesApiKeyId()
    {
        var billing = new StubBillingProvider();
        var auth = new StubAuthenticator
        {
            Result = ApiKeyAuthenticationResult.Success("user-1", "key-from-auth", "sk-or-test")
        };
        var client = CreateClient(auth, billing);

        await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

        billing.LastApiKeyId.Should().Be("key-from-auth");
    }

    [Fact]
    public async Task NonStreaming_PreferredNodeWhitespace_BothNull()
    {
        var control = new StubControlPlane();
        var client = CreateClient(new StubAuthenticator(), new StubBillingProvider(), control);

        var response = await PostChatAsync(
            client,
            """{"model":"gpt-4"}""",
            bearer: "sk-ok_key",
            preferredNodeId: "  ",
            preferredNodeControlToken: "  ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        control.LastPreferredNodeId.Should().BeNull();
        control.LastPreferredNodeControlToken.Should().BeNull();
    }

    // --- Helpers ---

    private HttpClient CreateClient(
        IApiKeyProvider? auth = null,
        IBillingProvider? billing = null,
        IControlPlaneProvider? control = null,
        IRouterProxyProvider? router = null,
        IUsageOutboxRepository? outbox = null)
    {
        return factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("SqlMigrations:Enabled", "false");
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IOptions<UsageOutboxOptions>>(
                    Options.Create(new UsageOutboxOptions { PollInterval = TimeSpan.FromHours(1) }));
                services.AddScoped<IApiKeyProvider>(_ => auth ?? new StubAuthenticator());
                services.AddScoped<IBillingProvider>(_ => billing ?? new StubBillingProvider());
                services.AddScoped<IUsageOutboxRepository>(_ => outbox ?? new InMemoryUsageOutboxRepository());
                services.AddScoped<IControlPlaneProvider>(_ => control ?? new StubControlPlane());
                services.AddScoped<IRouterProxyProvider>(_ => router ?? new StubRouterProxy());
            });
        }).CreateClient();
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
        public string? LastApiKeyId { get; private set; }

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string apiKeyId, string modelId, bool adminBypassDisabled, CancellationToken ct)
        {
            LastApiKeyId = apiKeyId;
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

        public Task<RouterProxyResponse> SendImageGenerationAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendTextToSpeechAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendMessagesAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, IHeaderDictionary? clientHeaders, string? queryString, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OpenRussiaRouter.ApiGateway.Anthropic.AnthropicStreamSession> BeginStreamMessagesAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, IHeaderDictionary? clientHeaders, string? queryString, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
