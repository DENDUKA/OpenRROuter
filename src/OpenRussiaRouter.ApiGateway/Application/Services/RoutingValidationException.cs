namespace OpenRussiaRouter.ApiGateway.Application.Services;

/// <summary>
/// Исключение валидации запроса внутри retry-цикла (например, unsupported reasoning parameters).
/// Ловится <see cref="RouterRetryExecutor"/> и превращается в 400 Bad Request.
/// </summary>
public sealed class RoutingValidationException(string message, string? errorCode = null)
    : Exception(message)
{
    public string? ErrorCode { get; } = errorCode;
}
