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
    /// <summary>
    /// Полный пайплайн (legacy): аутентификация + биллинг + выбор ноды за один вызов.
    /// Используется там, где retry-цикл не нужен.
    /// </summary>
    Task<GatewayAuthorizedContext?> AuthorizeAndAcquireLeaseAsync(
        HttpContext context,
        string? model,
        string requestId,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Шаг 1: валидация модели + аутентификация + проверка баланса.
    /// Вызывается один раз до retry-цикла. При ошибке записывает ошибку в ответ.
    /// </summary>
    /// <returns>Результат авторизации или <c>null</c> + ошибка в ответе.</returns>
    Task<GatewayAuthorizationResult?> AuthorizeAsync(
        HttpContext context,
        string? model,
        string requestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Шаг 2: выбор ноды с учётом исключённых.
    /// Вызывается на каждой итерации retry-цикла. При ошибке записывает ошибку в ответ.
    /// </summary>
    /// <returns>Полный контекст с лизой или <c>null</c> + ошибка в ответе.</returns>
    Task<GatewayAuthorizedContext?> AcquireLeaseAsync(
        HttpContext context,
        GatewayAuthorizationResult authorization,
        string requestId,
        IReadOnlyCollection<string>? excludedNodeIds,
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
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken)
    {
        var authResult = await AuthorizeAsync(context, model, requestId, cancellationToken);
        if (authResult is null)
            return null;

        return await AcquireLeaseAsync(context, authResult, requestId, excludedNodeIds, cancellationToken);
    }

    public async Task<GatewayAuthorizationResult?> AuthorizeAsync(
        HttpContext context,
        string? model,
        string requestId,
        CancellationToken cancellationToken)
    {
        var isAnthropic = context.Request.Path.StartsWithSegments("/v1/messages", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(model))
        {
            if (isAnthropic)
                await GatewayErrors.WriteAnthropicAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
            else
                await GatewayErrors.WriteAsync(context, "invalid_request_error", ErrorCodes.ValidationFailed, "model is required", requestId);
            return null;
        }

        var keyStatus = ApiKeyReader.TryRead(context.Request, out var apiKey);
        if (keyStatus == ApiKeyReadStatus.Conflict)
        {
            if (isAnthropic)
                await GatewayErrors.WriteAnthropicAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Conflicting API keys in Authorization and x-api-key headers", requestId);
            else
                await GatewayErrors.WriteAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Conflicting API keys in Authorization and x-api-key headers", requestId);
            return null;
        }

        if (keyStatus != ApiKeyReadStatus.Success)
        {
            if (isAnthropic)
                await GatewayErrors.WriteAnthropicAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token or x-api-key required", requestId);
            else
                await GatewayErrors.WriteAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token or x-api-key required", requestId);
            return null;
        }

        var authResult = await authProvider.AuthenticateAsync(apiKey, cancellationToken);
        if (!authResult.Succeeded)
        {
            if (isAnthropic)
                await GatewayErrors.WriteAnthropicAsync(context, "authentication_error", authResult.ErrorCode ?? ErrorCodes.AuthenticationRequired, "Invalid API key", requestId);
            else
                await GatewayErrors.WriteAsync(context, "authentication_error", authResult.ErrorCode ?? ErrorCodes.AuthenticationRequired, "Invalid API key", requestId);
            return null;
        }

        var adminBypassDisabled = authResult.IsAdmin && IsAdminTestHeaderPresent(context);
        var balanceResult = await billingProvider.CheckBalanceAndModelAsync(authResult.UserId!, authResult.ApiKeyId!, model, adminBypassDisabled, cancellationToken);
        if (!balanceResult.Allowed || !string.IsNullOrWhiteSpace(balanceResult.ErrorCode))
        {
            var (errorType, errorMessage) = balanceResult.ErrorCode switch
            {
                ErrorCodes.InsufficientBalance => ("billing_error", "Insufficient balance to process this request."),
                ErrorCodes.ApiKeyLimitExceeded => ("billing_error", "API key spend limit reached."),
                ErrorCodes.ModelUnavailable => ("invalid_request_error", $"Model '{model}' is not available."),
                ErrorCodes.InternalError => ("internal_error", "An internal error occurred while checking balance."),
                _ => ("invalid_request_error", "Balance check failed.")
            };
            if (isAnthropic)
                await GatewayErrors.WriteAnthropicAsync(context, errorType, balanceResult.ErrorCode ?? ErrorCodes.ValidationFailed, errorMessage, requestId);
            else
                await GatewayErrors.WriteAsync(context, errorType, balanceResult.ErrorCode ?? ErrorCodes.ValidationFailed, errorMessage, requestId);
            return null;
        }

        var (preferredNodeId, preferredNodeControlToken) = ReadPreferredNode(context);

        return new GatewayAuthorizationResult(
            apiKey,
            authResult.UserId!,
            authResult.ApiKeyId!,
            authResult.ApiKeyPrefix!,
            balanceResult.RouterModelId,
            preferredNodeId,
            preferredNodeControlToken,
            balanceResult.SupportsReasoning,
            balanceResult.AllowedReasoningEfforts,
            balanceResult.DefaultReasoningEffort,
            authResult.IsAdmin);
    }

    public async Task<GatewayAuthorizedContext?> AcquireLeaseAsync(
        HttpContext context,
        GatewayAuthorizationResult authorization,
        string requestId,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken)
    {
        var lease = await controlPlaneProvider.SelectRouterNodeAsync(
            authorization.RouterModelId,
            requestId,
            authorization.PreferredNodeId,
            authorization.PreferredNodeControlToken,
            excludedNodeIds,
            cancellationToken);
        if (lease is null)
        {
            var isAnthropic = context.Request.Path.StartsWithSegments("/v1/messages", StringComparison.OrdinalIgnoreCase);
            if (isAnthropic)
                await GatewayErrors.WriteAnthropicAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
            else
                await GatewayErrors.WriteAsync(context, "service_unavailable", ErrorCodes.RouterUnavailable, "No router available", requestId);
            return null;
        }

        return new GatewayAuthorizedContext(
            authorization.ApiKey,
            authorization.UserId,
            authorization.ApiKeyId,
            authorization.ApiKeyPrefix,
            authorization.RouterModelId,
            authorization.PreferredNodeId,
            authorization.PreferredNodeControlToken,
            lease,
            authorization.SupportsReasoning,
            authorization.AllowedReasoningEfforts,
            authorization.DefaultReasoningEffort,
            authorization.IsAdmin);
    }

    private const string AdminTestHeader = "X-ORR-Admin-Test";

    private static bool IsAdminTestHeaderPresent(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(AdminTestHeader, out var value))
            return false;
        return string.Equals(value.ToString().Trim(), "1", StringComparison.Ordinal);
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
