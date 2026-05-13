using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.ApiGateway.Auth;

public sealed class GrpcApiKeyAuthenticator(
    AuthGrpc.AuthServiceClient authClient,
    ILogger<GrpcApiKeyAuthenticator> logger) : IApiKeyAuthenticator
{

    public async Task<ApiKeyAuthenticationResult> AuthenticateAsync(
        string rawApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawApiKey))
        {
            return ApiKeyAuthenticationResult.Failed(ErrorCodes.InvalidApiKey);
        }

        try
        {
            var response = await authClient.ValidateApiKeyAsync(
                new ValidateApiKeyRequest { RawKey = rawApiKey },
                cancellationToken: cancellationToken);

            if (!response.IsValid)
            {
                return ApiKeyAuthenticationResult.Failed(
                    string.IsNullOrWhiteSpace(response.ErrorCode)
                        ? ErrorCodes.InvalidApiKey
                        : response.ErrorCode);
            }

            return ApiKeyAuthenticationResult.Success(response.UserId, response.ApiKeyId);
        }
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Auth service API key validation failed. RpcStatus: {RpcStatus}", ex.StatusCode);
            return ApiKeyAuthenticationResult.Failed(ErrorCodes.AuthenticationRequired);
        }
    }
}

