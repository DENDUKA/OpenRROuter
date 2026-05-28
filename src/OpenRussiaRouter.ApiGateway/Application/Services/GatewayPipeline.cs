using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Application.Services;

/// <summary>
/// Общий пайплайн авторизации запроса: валидация модели → аутентификация → проверка баланса → выбор ноды.
/// Возвращает <see langword="null"/> и записывает ошибку в ответ, если любой из шагов не пройден.
/// </summary>
public interface IGatewayPipeline
{
    Task<GatewayAuthorizedContext?> AuthorizeAndAcquireLeaseAsync(
        HttpContext context,
        string? model,
        string requestId,
        CancellationToken cancellationToken);
}

public sealed class GatewayPipeline(
    IApiKeyProvider authProvider,
    IBillingProvider billingProvider,
    IControlPlaneProvider controlPlaneProvider) : IGatewayPipeline
{
    public async Task<GatewayAuthorizedContext?> AuthorizeAndAcquireLeaseAsync(
        HttpContext context,
        string? model,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            await GatewayErrors.WriteAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
            return null;
        }

        if (!BearerTokenReader.TryRead(context.Request, out var apiKey))
        {
            await GatewayErrors.WriteAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token required", requestId);
            return null;
        }

        var authResult = await authProvider.AuthenticateAsync(apiKey, cancellationToken);
        if (!authResult.Succeeded)
        {
            await GatewayErrors.WriteAsync(context, "authentication_error", authResult.ErrorCode ?? ErrorCodes.AuthenticationRequired, "Invalid API key", requestId);
            return null;
        }

        var balanceResult = await billingProvider.CheckBalanceAndModelAsync(authResult.UserId!, model, cancellationToken);
        if (!balanceResult.Allowed || !string.IsNullOrWhiteSpace(balanceResult.ErrorCode))
        {
            var (errorType, errorMessage) = balanceResult.ErrorCode switch
            {
                ErrorCodes.InsufficientBalance => ("billing_error", "Insufficient balance to process this request."),
                ErrorCodes.ModelUnavailable    => ("invalid_request_error", $"Model '{model}' is not available."),
                ErrorCodes.InternalError       => ("internal_error", "An internal error occurred while checking balance."),
                _                              => ("invalid_request_error", "Balance check failed.")
            };
            await GatewayErrors.WriteAsync(context, errorType, balanceResult.ErrorCode ?? ErrorCodes.ValidationFailed, errorMessage, requestId);
            return null;
        }

        // Use RouterModelId (upstream node ID) so ControlPlane capability store can match
        // against what the node actually reports (e.g. "glm-4.7"), not the billing public ID
        // (e.g. "ORR/GLM-4.7").
        var (preferredNodeId, preferredNodeControlToken) = ReadPreferredNode(context);
        var lease = await controlPlaneProvider.SelectRouterNodeAsync(
            balanceResult.RouterModelId,
            requestId,
            preferredNodeId,
            preferredNodeControlToken,
            cancellationToken);
        if (lease is null)
        {
            await GatewayErrors.WriteAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
            return null;
        }

        return new GatewayAuthorizedContext(
            apiKey,
            authResult.UserId!,
            authResult.ApiKeyId!,
            authResult.ApiKeyPrefix!,
            balanceResult.RouterModelId,
            preferredNodeId,
            preferredNodeControlToken,
            lease);
    }

    private static (string? NodeId, string? ControlToken) ReadPreferredNode(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (!headers.TryGetValue(GatewayPreferredNodeHeaders.NodeId, out var nodeId) ||
            !headers.TryGetValue(GatewayPreferredNodeHeaders.NodeControlToken, out var controlToken))
        {
            return (null, null);
        }

        var normalizedNodeId = nodeId.ToString().Trim();
        var normalizedControlToken = controlToken.ToString().Trim();
        return string.IsNullOrWhiteSpace(normalizedNodeId) ||
               string.IsNullOrWhiteSpace(normalizedControlToken)
            ? (null, null)
            : (normalizedNodeId, normalizedControlToken);
    }
}
