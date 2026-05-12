using OpenRussiaRouter.AuthService.Application;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class GrpcAuthServiceTests
{
    [Fact]
    public async Task ValidateApiKey_ValidKey_ReturnsGrpcResponse()
    {
        var service = new GrpcAuthService(new StubValidator(new ValidateApiKeyResult(true, "user-1", "key-1", null)));

        var response = await service.ValidateApiKey(new ValidateApiKeyRequest { RawKey = "orr_test" }, context: null!);

        Assert.True(response.IsValid);
        Assert.Equal("user-1", response.UserId);
        Assert.Equal("key-1", response.ApiKeyId);
        Assert.Equal(string.Empty, response.ErrorCode);
    }

    [Fact]
    public async Task ValidateApiKey_InvalidKey_ReturnsErrorCode()
    {
        var service = new GrpcAuthService(new StubValidator(new ValidateApiKeyResult(false, null, null, "invalid_api_key")));

        var response = await service.ValidateApiKey(new ValidateApiKeyRequest { RawKey = "orr_bad" }, context: null!);

        Assert.False(response.IsValid);
        Assert.Equal(string.Empty, response.UserId);
        Assert.Equal(string.Empty, response.ApiKeyId);
        Assert.Equal("invalid_api_key", response.ErrorCode);
    }

    private sealed class StubValidator(ValidateApiKeyResult result) : IAuthApiKeyValidator
    {
        public ValidateApiKeyResult Validate(string rawKey) => result;
    }
}

