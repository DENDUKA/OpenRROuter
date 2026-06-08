using OpenRussiaRouter.ApiGateway.ControlPlane;

namespace OpenRussiaRouter.ApiGateway.Application.Services;

/// <summary>Контекст успешно авторизованного запроса: ключ, пользователь и lease ноды.</summary>
public sealed record GatewayAuthorizedContext(
    string ApiKey,
    string UserId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string RouterModelId,
    string? PreferredNodeId,
    string? PreferredNodeControlToken,
    GatewayRouterLease Lease,
    bool SupportsReasoning,
    IReadOnlyList<string>? AllowedReasoningEfforts,
    bool IsAdmin = false);
