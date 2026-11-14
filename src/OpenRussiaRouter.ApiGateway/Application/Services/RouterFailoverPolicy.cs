using System.Net;

namespace OpenRussiaRouter.ApiGateway.Application.Services;

/// <summary>
/// Единая для всех retry-циклов gateway политика failover между нодами 9Router.
/// Вынесена в одно место, чтобы нестриминговый путь (<see cref="RouterRetryExecutor"/>),
/// streaming chat и /v1/messages не разъезжались в семантике.
/// </summary>
internal static class RouterFailoverPolicy
{
    /// <summary>
    /// Определяет, нужно ли после ответа/ошибки ноды искать другую ноду:
    /// <list type="bullet">
    /// <item>5xx — нода или её провайдер сломан;</item>
    /// <item>429 — лимит этой ноды, другая может быть свободна;</item>
    /// <item>404 — нода не обслуживает модель (нет активных credentials провайдера
    /// или модели нет в каталоге ноды), а другая нода может её иметь.</item>
    /// </list>
    /// Остальные 4xx — клиентские ошибки (валидация, аутентификация): они повторятся
    /// на любой ноде, поэтому отдаются клиенту как есть.
    /// </summary>
    public static bool ShouldFailOver(int statusCode)
        => statusCode >= 500
           || statusCode == (int)HttpStatusCode.TooManyRequests
           || statusCode == (int)HttpStatusCode.NotFound;
}
