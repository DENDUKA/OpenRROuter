using Grpc.Core;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.ApiGateway.Auth;

/// <summary>
/// API key authenticator backed by Auth Service gRPC API.
/// </summary>
public sealed class GrpcApiKeyAuthenticator(AuthGrpc.AuthServiceClient authClient) : IApiKeyAuthenticator
{
    /// <inheritdoc />
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
        catch (RpcException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiKeyAuthenticationResult.Failed(ErrorCodes.AuthenticationRequired);
        }
    }
}

