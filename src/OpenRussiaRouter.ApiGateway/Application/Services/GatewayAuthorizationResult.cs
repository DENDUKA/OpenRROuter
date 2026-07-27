using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.Application.Services;

/// <summary>
/// Результат аутентификации и проверки баланса (без выбора ноды).
/// Используется для мемоизации внутри одного запроса: аутентификация и биллинг
/// выполняются один раз до retry-цикла, выбор ноды — на каждой итерации.
/// </summary>
public sealed record GatewayAuthorizationResult(
    string ApiKey,
    string UserId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string RouterModelId,
    string? PreferredNodeId,
    string? PreferredNodeControlToken,
    bool SupportsReasoning,
    IReadOnlyList<string>? AllowedReasoningEfforts,
    string? DefaultReasoningEffort,
    bool IsAdmin = false);

/// <summary>Результат неудачной авторизации с кодом ошибки.</summary>
public sealed record GatewayAuthorizationFailure(
    string ErrorType,
    string ErrorCode,
    string ErrorMessage);
