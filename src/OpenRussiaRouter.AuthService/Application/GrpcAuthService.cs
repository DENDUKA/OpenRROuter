using Grpc.Core;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.AuthService.Application;

/// <summary>
/// gRPC Auth API used by API Gateway and internal services.
/// </summary>
public sealed class GrpcAuthService(IAuthApiKeyValidator apiKeyValidator) : AuthGrpc.AuthServiceBase
{
    /// <inheritdoc />
    public override Task<ValidateApiKeyResponse> ValidateApiKey(
        ValidateApiKeyRequest request,
        ServerCallContext context)
    {
        var result = apiKeyValidator.Validate(request.RawKey);

        return Task.FromResult(new ValidateApiKeyResponse
        {
            IsValid = result.IsValid,
            UserId = result.UserId ?? string.Empty,
            ApiKeyId = result.ApiKeyId ?? string.Empty,
            ErrorCode = result.ErrorCode ?? string.Empty
        });
    }
}

