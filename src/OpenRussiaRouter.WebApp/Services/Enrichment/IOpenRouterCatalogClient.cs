namespace OpenRussiaRouter.WebApp.Services.Enrichment;

public interface IOpenRouterCatalogClient
{
    /// <summary>
    /// Загружает весь каталог OpenRouter и возвращает список.
    /// При сетевой ошибке возвращает пустой список (не бросает).
    /// </summary>
    Task<IReadOnlyList<OpenRouterModelEntry>> LoadCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ищет модель по upstream_model_id.
    /// Стратегия: exact match → strip-suffix match.
    /// Возвращает null если модель не найдена.
    /// </summary>
    OpenRouterMatchResult? FindMatch(IReadOnlyList<OpenRouterModelEntry> catalog, string upstreamModelId);
}