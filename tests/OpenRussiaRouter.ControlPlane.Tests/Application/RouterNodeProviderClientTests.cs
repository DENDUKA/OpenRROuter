using System.Net;
using FluentAssertions;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;
using OpenRussiaRouter.ControlPlane.Infrastructure.Http;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

public sealed class RouterNodeProviderTests
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

        handler.LastMethod.Should().Be(HttpMethod.Get);
        handler.LastUri.Should().Be("https://router.example/api/integrations/providers/quota?includeInactive=true");
        handler.Authorization.Should().Be("Bearer control-token");
        var provider = result.Providers.Should().ContainSingle().Which;
        provider.ConnectionId.Should().Be("conn-1");
        provider.Usage?.GetProperty("message").GetString().Should().Be("ok");
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

        handler.LastMethod.Should().Be(HttpMethod.Patch);
        handler.LastUri.Should().Be("https://router.example/api/integrations/providers/conn-1/status");
        handler.Authorization.Should().Be("Bearer control-token");
        handler.Body.Should().Be("""{"isActive":false}""");
        result.Connection?.Id.Should().Be("conn-1");
        result.Connection?.IsActive.Should().BeFalse();
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

        handler.LastMethod.Should().Be(HttpMethod.Get);
        handler.LastUri.Should().Be("https://router.example/api/integrations/providers/by-type/minimax/models?source=static&includeCompatible=true");
        handler.Authorization.Should().Be("Bearer control-token");
        var model = result.Models.Should().ContainSingle().Which;
        model.Id.Should().Be("MiniMax-M2.7");
        model.PublicId.Should().Be("minimax/MiniMax-M2.7");
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

        handler.LastMethod.Should().Be(HttpMethod.Get);
        handler.LastUri.Should().Be("https://router.example/api/integrations/keys");
        handler.Authorization.Should().Be("Bearer control-token");
        result.Keys.Should().ContainSingle().Which.Id.Should().Be("key-1");
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

        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri.Should().Be("https://router.example/api/integrations/keys");
        handler.Authorization.Should().Be("Bearer control-token");
        handler.Body.Should().Be("""{"name":"OpenRussiaRouter issued key","key":"sk-or-tenant-123-fixed-token"}""");
        result.Created.Should().BeTrue();
        result.Id.Should().Be("key-1");
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

        handler.LastMethod.Should().Be(HttpMethod.Patch);
        handler.LastUri.Should().Be("https://router.example/api/integrations/keys/key-1/status");
        handler.Authorization.Should().Be("Bearer control-token");
        handler.Body.Should().Be("""{"isActive":false}""");
        result.Id.Should().Be("key-1");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ListIntegrationKeysAsync_MissingToken_ThrowsAtNodeConstruction()
    {
        ((Action)(() => CreateNode(string.Empty))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ListIntegrationKeysAsync_ErrorResponse_ThrowsProviderException()
    {
        var handler = new CapturingHandler(JsonResponse(
            """{ "error": "Unauthorized" }""",
            HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        var ex = await ((Func<Task>)(() => client.ListIntegrationKeysAsync(CreateNode(), CancellationToken.None))).Should().ThrowAsync<RouterNodeProviderException>();

        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.RouterError.Should().Be("Unauthorized");
        ex.Which.Message.Should().NotContain("control-token");
    }

    [Fact]
    public async Task ListProvidersAsync_SendsGetToProvidersEndpoint()
    {
        var handler = new CapturingHandler(JsonResponse("""
            {
              "connections": [
                {
                  "id": "conn-1",
                  "provider": "minimax",
                  "authType": "apikey",
                  "name": "MiniMax account",
                  "isActive": true,
                  "testStatus": "active",
                  "providerSpecificData": {
                    "prefix": "minimax"
                  }
                }
              ]
            }
            """));
        var client = CreateClient(handler);

        var result = await client.ListProvidersAsync(CreateNode(), CancellationToken.None);

        handler.LastMethod.Should().Be(HttpMethod.Get);
        handler.LastUri.Should().Be("https://router.example/api/providers");
        handler.Authorization.Should().Be("Bearer control-token");
        var conn = result.Connections.Should().ContainSingle().Which;
        conn.Id.Should().Be("conn-1");
        conn.Provider.Should().Be("minimax");
        conn.AuthType.Should().Be("apikey");
        conn.Name.Should().Be("MiniMax account");
        conn.IsActive.Should().BeTrue();
        conn.TestStatus.Should().Be("active");
        conn.ProviderSpecificData!.Prefix.Should().Be("minimax");
    }

    [Fact]
    public async Task ListProvidersAsync_DeserializesProviderSpecificDataAllFields()
    {
        var handler = new CapturingHandler(JsonResponse("""
            {
              "connections": [
                {
                  "id": "compatible-conn",
                  "provider": "openai-compatible-uuid",
                  "authType": "apikey",
                  "name": "Local OpenAI-compatible node",
                  "isActive": true,
                  "testStatus": "active",
                  "providerSpecificData": {
                    "prefix": "myllm",
                    "baseUrl": "http://localhost:1234/v1",
                    "nodeName": "Local LLM"
                  }
                }
              ]
            }
            """));
        var client = CreateClient(handler);

        var result = await client.ListProvidersAsync(CreateNode(), CancellationToken.None);

        var conn = result.Connections.Should().ContainSingle().Which;
        conn.ProviderSpecificData.Should().NotBeNull();
        conn.ProviderSpecificData!.Prefix.Should().Be("myllm");
        conn.ProviderSpecificData.BaseUrl.Should().Be("http://localhost:1234/v1");
        conn.ProviderSpecificData.NodeName.Should().Be("Local LLM");
    }

    [Fact]
    public async Task ListProvidersAsync_ErrorResponse_ThrowsProviderException()
    {
        var handler = new CapturingHandler(JsonResponse(
            """{ "error": "Failed to fetch providers" }""",
            HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        var ex = await ((Func<Task>)(() => client.ListProvidersAsync(CreateNode(), CancellationToken.None)))
            .Should().ThrowAsync<RouterNodeProviderException>();

        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.RouterError.Should().Be("Failed to fetch providers");
    }

    private static RouterNodeProvider CreateClient(CapturingHandler handler)
    {
        return new RouterNodeProvider(new HttpClient(handler));
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
