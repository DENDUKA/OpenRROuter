namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <summary>
/// Одна модель одного подключения одной ноды и её состояние в каталоге ORR. Отвечает на вопрос
/// «что нода реально умеет и что из этого мы продаём» — <see cref="QuotaMeasurement"/> этого не знает
/// (там модели выводятся из имён квот), а <see cref="ActiveModelQuotaMeasurement"/> видит только
/// включённые модели.
/// </summary>
/// <param name="ModelId">Публичный id каталога (RWT/GLM-4.7), либо routable id ноды, если в каталоге модели нет.</param>
/// <param name="BillingState">Код из <see cref="ModelBillingStates"/>.</param>
public sealed record NodeModelCatalogEntry(
    string NodeId,
    string Provider,
    string ConnectionId,
    string ModelId,
    string RouterModelId,
    string BillingState);

/// <summary>
/// Замкнутый список состояний — это значения метки Prometheus, произвольные строки сюда попадать
/// не должны (по образцу <see cref="QuotaUnavailableReasons"/>).
/// </summary>
public static class ModelBillingStates
{
    /// <summary>Модель есть в каталоге ORR и включена.</summary>
    public const string Enabled = "enabled";

    /// <summary>Модель есть в каталоге ORR, но выключена.</summary>
    public const string Disabled = "disabled";

    /// <summary>Нода модель отдаёт, а в каталоге ORR её нет.</summary>
    public const string Absent = "absent";
}
