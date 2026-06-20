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

/// <summary>
/// Per-key / per-IP rate limiting on the public chat-completions endpoint (the balance-touching
/// surface). Each test builds its own host so partition state does not leak between tests.
/// </summary>
public sealed class RateLimitingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public RateLimitingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ChatCompletions_ExceedingPerKeyLimit_Returns429WithOpenAiRateLimitError()
    {
        var client = CreateClient(permitLimit: 2);

        (await PostChatAsync(client, "sk-key-a")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostChatAsync(client, "sk-key-a")).StatusCode.Should().Be(HttpStatusCode.OK);

        var limited = await PostChatAsync(client, "sk-key-a");

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains("Retry-After").Should().BeTrue();

        var doc = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("type").GetString().Should().Be("rate_limit_error");
        error.GetProperty("code").GetString().Should().Be(ErrorCodes.RateLimited);
    }

    [Fact]
    public async Task ChatCompletions_DifferentKeys_HaveIndependentBuckets()
    {
        var client = CreateClient(permitLimit: 1);

        (await PostChatAsync(client, "sk-key-a")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostChatAsync(client, "sk-key-a")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // A different key gets its own fresh window.
        (await PostChatAsync(client, "sk-key-b")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChatCompletions_RateLimitingDisabled_DoesNotThrottle()
    {
        var client = CreateClient(permitLimit: 1, enabled: false);

        for (var i = 0; i < 3; i++)
        {
            (await PostChatAsync(client, "sk-key-a")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // --- Helpers ---

    private HttpClient CreateClient(int permitLimit, bool enabled = true)
    {
        return factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:Enabled", enabled ? "true" : "false");
            b.UseSetting("RateLimiting:PermitLimit", permitLimit.ToString());
            b.UseSetting("RateLimiting:WindowSeconds", "60");
            b.ConfigureTestServices(services =>
            {
                services.AddScoped<IApiKeyProvider>(_ => new StubAuthenticator());
                services.AddScoped<IBillingProvider>(_ => new StubBillingProvider());
                services.AddScoped<IControlPlaneProvider>(_ => new StubControlPlane());
                services.AddScoped<IRouterProxyProvider>(_ => new StubRouterProxy());
            });
        }).CreateClient();
    }

    private static async Task<HttpResponseMessage> PostChatAsync(HttpClient client, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent("""{"model":"gpt-4"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    // --- Test doubles (success path so under-limit requests return 200) ---

    private sealed class StubAuthenticator : IApiKeyProvider
    {
        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test"));
    }

    private sealed class StubBillingProvider : IBillingProvider
    {
        public Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken ct)
            => Task.FromResult((IReadOnlyCollection<GatewayPublicModel>)Array.Empty<GatewayPublicModel>());

        public Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(string userId, string apiKeyId, string modelId, bool adminBypassDisabled, CancellationToken ct)
            => Task.FromResult(new GatewayBalanceCheckResult(true, "gpt-4", "gpt-4", "v1", MoneyAmount.Usd(100), null));

        public Task<bool> RecordUsageAsync(GatewayUsageEvent ev, CancellationToken ct)
            => Task.FromResult(true);
    }

    private sealed class StubControlPlane : IControlPlaneProvider
    {
        public Task<GatewayRouterLease?> SelectRouterNodeAsync(
            string modelId, string requestId, string? preferredNodeId, string? preferredNodeControlToken,
            IReadOnlyCollection<string>? excludedNodeIds, CancellationToken ct)
            => Task.FromResult<GatewayRouterLease?>(new("lease-1", "node-1", new Uri("https://router.example"), "secret"));

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubRouterProxy : IRouterProxyProvider
    {
        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken ct)
            => Task.FromResult(new RouterProxyResponse(200, "application/json", """{"id":"chatcmpl-1","choices":[]}"""));

        public Task<RouterStreamSession> BeginStreamChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendImageGenerationAsync(
            GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendTextToSpeechAsync(
            GatewayRouterLease lease, string requestJson, string requestId, string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
