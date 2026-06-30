using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.ApiGateway.Tests.Integration;

public sealed class TextToSpeechIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TextToSpeechIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Tts_HappyPath_UsageEventCarriesTtsEndpointAndStreamFalse()
    {
        var router = new StubRouterProxy
        {
            Response = new RouterProxyResponse(200, "audio/mpeg", string.Empty, BodyBytes: new byte[] { 1, 2, 3 })
        };
        var billing = new StubBillingProvider();
        var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

        var response = await PostTtsAsync(client, """{"model":"tts-1","input":"hello world","voice":"alloy"}""", bearer: "sk-ok_key");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(50);
        billing.LastUsageEvent.Should().NotBeNull();
        billing.LastUsageEvent!.Endpoint.Should().Be("tts");
        billing.LastUsageEvent.Stream.Should().BeFalse();
        // Output tokens billed as Unicode character count of the input.
        billing.LastUsageEvent.Usage.OutputTokens.Should().Be("hello world".Length);
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

    private static async Task<HttpResponseMessage> PostTtsAsync(HttpClient client, string body, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/speech")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    // --- Test doubles ---

    private sealed class StubAuthenticator : IApiKeyProvider
    {
        public Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken ct)
            => Task.FromResult(ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test"));
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

    private sealed class StubControlPlane : IControlPlaneProvider
    {
        public GatewayRouterLease? Lease { get; set; } =
            new("lease-1", "node-1", new Uri("https://router.example"), "secret");

        public Task<GatewayRouterLease?> SelectRouterNodeAsync(
            string modelId,
            string requestId,
            string? preferredNodeId,
            string? preferredNodeControlToken,
            IReadOnlyCollection<string>? excludedNodeIds,
            CancellationToken ct)
            => Task.FromResult(Lease);

        public Task ReleaseRouterNodeAsync(string leaseId, string status, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubRouterProxy : IRouterProxyProvider
    {
        public RouterProxyResponse Response { get; set; } =
            new(200, "audio/mpeg", string.Empty, BodyBytes: new byte[] { 1, 2, 3 });

        public Task<RouterProxyResponse> SendChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterStreamSession> BeginStreamChatCompletionAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendImageGenerationAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RouterProxyResponse> SendTextToSpeechAsync(
            GatewayRouterLease lease, string requestJson, string requestId,
            string userApiKey, CancellationToken ct)
            => Task.FromResult(Response);
    }
}
