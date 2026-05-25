using System.Net;
using FluentAssertions;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure.Http;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class CachedRouterNodeProviderTests
{
    [Fact]
    public async Task ListProvidersAsync_RepeatedCallWithinTtl_UsesCachedResponse()
    {
        var handler = new CountingHandler(_ => JsonResponse("""
            {
              "connections": [
                {
                  "id": "conn-1",
                  "provider": "minimax",
                  "authType": "apikey",
                  "name": "MiniMax account",
                  "isActive": true
                }
              ]
            }
            """));
        var provider = CreateProvider(handler);

        var first = await provider.ListProvidersAsync(CreateNode(), CancellationToken.None);
        var second = await provider.ListProvidersAsync(CreateNode(), CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        first.Connections.Should().ContainSingle().Which.Id.Should().Be("conn-1");
        second.Connections.Should().ContainSingle().Which.Id.Should().Be("conn-1");
    }

    [Fact]
    public async Task GetProviderQuotaAsync_DisabledCache_CallsInnerProviderEveryTime()
    {
        var handler = new CountingHandler(_ => JsonResponse("""{ "providers": [] }"""));
        var provider = CreateProvider(handler, new RouterNodeProviderCacheOptions { Enabled = false });

        await provider.GetProviderQuotaAsync(CreateNode(), includeInactive: true, CancellationToken.None);
        await provider.GetProviderQuotaAsync(CreateNode(), includeInactive: true, CancellationToken.None);

        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task ListModelsAsync_FailedRequest_IsNotCached()
    {
        var responses = new Queue<HttpResponseMessage>([
            JsonResponse("""{ "error": "temporary failure" }""", HttpStatusCode.ServiceUnavailable),
            JsonResponse("""{ "data": [ { "id": "gpt-test" } ] }""")
        ]);
        var handler = new CountingHandler(_ => responses.Dequeue());
        var provider = CreateProvider(handler);

        await ((Func<Task>)(() => provider.ListModelsAsync(CreateNode(), CancellationToken.None)))
            .Should().ThrowAsync<RouterNodeProviderException>();
        var result = await provider.ListModelsAsync(CreateNode(), CancellationToken.None);

        handler.RequestCount.Should().Be(2);
        result.Data.Should().ContainSingle().Which.Id.Should().Be("gpt-test");
    }

    [Fact]
    public async Task UpdateProviderStatusAsync_Success_InvalidatesNodeCache()
    {
        var responses = new Queue<HttpResponseMessage>([
            JsonResponse("""
            {
              "connections": [
                {
                  "id": "conn-1",
                  "provider": "minimax",
                  "authType": "apikey",
                  "name": "Old",
                  "isActive": true
                }
              ]
            }
            """),
            JsonResponse("""{ "connection": { "id": "conn-1", "isActive": false } }"""),
            JsonResponse("""
            {
              "connections": [
                {
                  "id": "conn-1",
                  "provider": "minimax",
                  "authType": "apikey",
                  "name": "New",
                  "isActive": false
                }
              ]
            }
            """)
        ]);
        var handler = new CountingHandler(_ => responses.Dequeue());
        var provider = CreateProvider(handler);
        var node = CreateNode();

        var first = await provider.ListProvidersAsync(node, CancellationToken.None);
        await provider.UpdateProviderStatusAsync(node, "conn-1", isActive: false, CancellationToken.None);
        var second = await provider.ListProvidersAsync(node, CancellationToken.None);

        handler.RequestCount.Should().Be(3);
        first.Connections.Should().ContainSingle().Which.Name.Should().Be("Old");
        second.Connections.Should().ContainSingle().Which.Name.Should().Be("New");
    }

    [Fact]
    public async Task ListIntegrationKeysAsync_RepeatedCallWithinTtl_UsesCachedResponse()
    {
        var handler = new CountingHandler(_ => JsonResponse("""
            {
              "keys": [
                {
                  "id": "key-1",
                  "name": "OpenRussiaRouter issued key",
                  "keyPreview": "sk-or...t0kn",
                  "machineId": "machine-1",
                  "isActive": true,
                  "managedBy": "openrussia-router",
                  "createdAt": "2026-05-17T12:00:00.000Z"
                }
              ]
            }
            """));
        var provider = CreateProvider(handler);

        await provider.ListIntegrationKeysAsync(CreateNode(), CancellationToken.None);
        await provider.ListIntegrationKeysAsync(CreateNode(), CancellationToken.None);

        handler.RequestCount.Should().Be(1);
    }

    private static CachedRouterNodeProvider CreateProvider(
        CountingHandler handler,
        RouterNodeProviderCacheOptions? options = null)
    {
        var inner = new RouterNodeProvider(new HttpClient(handler));
        return new CachedRouterNodeProvider(
            inner,
            new RouterNodeProviderCacheStore(),
            TestOptionsMonitor.Create(options ?? new RouterNodeProviderCacheOptions()));
    }

    private static RouterNode CreateNode()
    {
        return new RouterNode(
            "node-a",
            "node-a",
            new Uri("https://router.example"),
            "control-token",
            1,
            DateTimeOffset.UtcNow);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
