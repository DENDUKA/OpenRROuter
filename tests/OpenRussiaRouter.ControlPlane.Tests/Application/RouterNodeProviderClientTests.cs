using System.Net;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterNodeProviderClientTests
{
    [Fact]
    public async Task GetProviderQuotaAsync_IncludeInactive_SendsAuthorizedRequestAndDeserializesProviders()
    {
        var handler = new CapturingHandler(JsonResponse("""
            {
              "providers": [
                {
                  "connectionId": "conn-1",
                  "provider": "codex",
                  "name": "Work Codex",
                  "authType": "oauth",
                  "isActive": true,
                  "testStatus": "active",
                  "quotaSupported": true,
                  "usage": { "message": "ok" },
                  "error": null
                }
              ]
            }
            """));
        var client = CreateClient(handler);

        var result = await client.GetProviderQuotaAsync(CreateNode(), includeInactive: true, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("https://router.example/api/integrations/providers/quota?includeInactive=true", handler.LastUri);
        Assert.Equal("Bearer control-token", handler.Authorization);
        var provider = Assert.Single(result.Providers);
        Assert.Equal("conn-1", provider.ConnectionId);
        Assert.Equal("ok", provider.Usage?.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateProviderStatusAsync_SendsPatchStatusPayload()
    {
        var handler = new CapturingHandler(JsonResponse("""
            {
              "connection": {
                "id": "conn-1",
                "provider": "codex",
                "name": "Work Codex",
                "authType": "oauth",
                "isActive": false,
                "testStatus": "active",
                "providerSpecificData": { "baseUrl": "https://example.test" }
              }
            }
            """));
        var client = CreateClient(handler);

        var result = await client.UpdateProviderStatusAsync(CreateNode(), "conn-1", isActive: false, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.LastMethod);
        Assert.Equal("https://router.example/api/integrations/providers/conn-1/status", handler.LastUri);
        Assert.Equal("Bearer control-token", handler.Authorization);
        Assert.Equal("""{"isActive":false}""", handler.Body);
        Assert.Equal("conn-1", result.Connection?.Id);
        Assert.False(result.Connection?.IsActive);
    }

    [Fact]
    public async Task ListProviderModelsAsync_SourceAndCompatible_SendsExpectedQuery()
    {
        var handler = new CapturingHandler(JsonResponse("""
            {
              "provider": "minimax",
              "alias": "minimax",
              "source": "static",
              "models": [
                {
                  "id": "MiniMax-M2.7",
                  "publicId": "minimax/MiniMax-M2.7",
                  "name": "MiniMax M2.7",
                  "type": "llm",
                  "enabled": true
                }
              ]
            }
            """));
        var client = CreateClient(handler);

        var result = await client.ListProviderModelsAsync(
            CreateNode(),
            "minimax",
            source: "static",
            includeCompatible: true,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("https://router.example/api/integrations/providers/by-type/minimax/models?source=static&includeCompatible=true", handler.LastUri);
        Assert.Equal("Bearer control-token", handler.Authorization);
        var model = Assert.Single(result.Models);
        Assert.Equal("MiniMax-M2.7", model.Id);
        Assert.Equal("minimax/MiniMax-M2.7", model.PublicId);
    }

    [Fact]
    public async Task ListIntegrationKeysAsync_SendsExpectedRequestAndDeserializesKeys()
    {
        var handler = new CapturingHandler(JsonResponse("""
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
        var client = CreateClient(handler);

        var result = await client.ListIntegrationKeysAsync(CreateNode(), CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("https://router.example/api/integrations/keys", handler.LastUri);
        Assert.Equal("Bearer control-token", handler.Authorization);
        Assert.Equal("key-1", Assert.Single(result.Keys).Id);
    }

    [Fact]
    public async Task CreateIntegrationKeyAsync_SendsPostPayload()
    {
        var handler = new CapturingHandler(JsonResponse("""{ "created": true, "id": "key-1" }""", HttpStatusCode.Created));
        var client = CreateClient(handler);

        var result = await client.CreateIntegrationKeyAsync(
            CreateNode(),
            "OpenRussiaRouter issued key",
            "sk-or-tenant-123-fixed-token",
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://router.example/api/integrations/keys", handler.LastUri);
        Assert.Equal("Bearer control-token", handler.Authorization);
        Assert.Equal("""{"name":"OpenRussiaRouter issued key","key":"sk-or-tenant-123-fixed-token"}""", handler.Body);
        Assert.True(result.Created);
        Assert.Equal("key-1", result.Id);
    }

    [Fact]
    public async Task UpdateIntegrationKeyStatusAsync_SendsPatchPayload()
    {
        var handler = new CapturingHandler(JsonResponse("""
            {
              "id": "key-1",
              "name": "OpenRussiaRouter issued key",
              "keyPreview": "sk-or...t0kn",
              "machineId": "machine-1",
              "isActive": false,
              "managedBy": "openrussia-router",
              "createdAt": "2026-05-17T12:00:00.000Z"
            }
            """));
        var client = CreateClient(handler);

        var result = await client.UpdateIntegrationKeyStatusAsync(CreateNode(), "key-1", isActive: false, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.LastMethod);
        Assert.Equal("https://router.example/api/integrations/keys/key-1/status", handler.LastUri);
        Assert.Equal("Bearer control-token", handler.Authorization);
        Assert.Equal("""{"isActive":false}""", handler.Body);
        Assert.Equal("key-1", result.Id);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task ListIntegrationKeysAsync_MissingToken_DoesNotSendRequest()
    {
        var handler = new CapturingHandler(JsonResponse("""{ "keys": [] }"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ListIntegrationKeysAsync(CreateNode(string.Empty), CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ListIntegrationKeysAsync_ErrorResponse_ThrowsProviderException()
    {
        var handler = new CapturingHandler(JsonResponse(
            """{ "error": "Unauthorized" }""",
            HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<RouterNodeProviderException>(
            () => client.ListIntegrationKeysAsync(CreateNode(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("Unauthorized", ex.RouterError);
        Assert.DoesNotContain("control-token", ex.Message);
    }

    private static RouterNodeProviderClient CreateClient(CapturingHandler handler)
    {
        return new RouterNodeProviderClient(new HttpClient(handler));
    }

    private static RouterNode CreateNode(string controlToken = "control-token")
    {
        return new RouterNode(
            "node-a",
            "node-a",
            new Uri("https://router.example"),
            controlToken,
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

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return response;
        }
    }
}
