namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <summary>
/// Отчёт «отдаёт ли это подключение квоту вообще». Нужен потому, что провайдер, у которого
/// квоту прочитать не удалось, раньше просто исчезал из метрик — в Grafana это выглядело как
/// «квот нет», а не «квоты сломаны», и протухшие креды могли месяцами оставаться незамеченными.
/// </summary>
/// <param name="Reporting">true — квота прочитана; false — подключение есть, но чисел нет.</param>
/// <param name="Reason">Код из <see cref="QuotaUnavailableReasons"/> (не сырое сообщение ноды).</param>
public sealed record QuotaStatusMeasurement(
    string NodeId,
    string Provider,
    string ConnectionId,
    string AccountName,
    bool Reporting,
    string Reason);

/// <summary>
/// Замкнутый список причин — это значения метки Prometheus, поэтому сырое сообщение ноды
/// («Antigravity quota API authentication expired…») сюда попадать не должно: неограниченная
/// кардинальность. Сообщение целиком остаётся в админке ноды и в её ответе.
/// </summary>
public static class QuotaUnavailableReasons
{
    /// <summary>Квота прочитана нормально.</summary>
    public const string Ok = "ok";

    /// <summary>Нода не ответила на запрос квот целиком (таймаут/сеть/HTTP-ошибка).</summary>
    public const string NodeUnreachable = "node_unreachable";

    /// <summary>Подключение есть, но объекта usage в ответе нет.</summary>
    public const string NoUsage = "no_usage";

    /// <summary>usage.quotas присутствует, но пуст — типично для протухшей авторизации у провайдера.</summary>
    public const string EmptyQuotas = "empty_quotas";

    /// <summary>usage есть, но ни одного числового поля квоты в нём нет — обычно там только message об ошибке.</summary>
    public const string NoQuotaFields = "no_quota_fields";
}
