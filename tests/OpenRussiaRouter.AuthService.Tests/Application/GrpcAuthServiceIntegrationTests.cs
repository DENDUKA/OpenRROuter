using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class GrpcAuthServiceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public GrpcAuthServiceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AuthGrpcServer_InviteApiKeyWorkflow_ReturnsExpectedResponses()
    {
        var client = CreateClient();

        var invite = await client.CreateInviteAsync(new CreateInviteRequest
        {
            Email = "USER@example.com",
            Role = "User",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        });
        var user = await client.CompleteInviteAsync(new CompleteInviteRequest
        {
            RawToken = invite.RawToken,
            Password = "StrongPassword123!"
        });
        var apiKey = await client.CreateApiKeyAsync(new CreateApiKeyRequest
        {
            UserId = user.UserId,
            Name = "default"
        });
        var validation = await client.ValidateApiKeyAsync(new ValidateApiKeyRequest { RawKey = apiKey.RawKey });
        var revoked = await client.RevokeApiKeyAsync(new RevokeApiKeyRequest
        {
            UserId = user.UserId,
            ApiKeyId = apiKey.ApiKeyId
        });
        var revokedValidation = await client.ValidateApiKeyAsync(new ValidateApiKeyRequest { RawKey = apiKey.RawKey });

        Assert.True(invite.IsCreated);
        Assert.Equal("user@example.com", invite.Email);
        Assert.True(user.IsCompleted);
        Assert.Equal("user@example.com", user.Email);
        Assert.True(apiKey.IsCreated);
        Assert.True(validation.IsValid);
        Assert.Equal(user.UserId, validation.UserId);
        Assert.Equal(apiKey.ApiKeyId, validation.ApiKeyId);
        Assert.True(revoked.IsRevoked);
        Assert.False(revokedValidation.IsValid);
        Assert.Equal("invalid_api_key", revokedValidation.ErrorCode);
    }

    [Fact]
    public async Task AuthGrpcServer_CreateInviteInvalidRole_ReturnsValidationFailed()
    {
        var client = CreateClient();

        var response = await client.CreateInviteAsync(new CreateInviteRequest
        {
            Email = "user@example.com",
            Role = "Owner",
            CreatedByAdminId = "admin-1",
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        });

        Assert.False(response.IsCreated);
        Assert.Equal("validation_failed", response.ErrorCode);
    }

    private global::OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService.AuthServiceClient CreateClient()
    {
        var httpClient = factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        return new global::OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService.AuthServiceClient(channel);
    }
}
