namespace OpenRussiaRouter.CollectorService.Application.Billing;

/// <summary>
/// Модель из каталога биллинга — включая выключенные. Выключенные нужны, чтобы отличить
/// «модель у нас есть, но продажа выключена» от «ноду отдаёт, а в каталоге её вообще нет»
/// (метка billing_state метрики orr_node_model_catalog).
/// </summary>
public sealed record BillingCatalogModel(string ModelId, string RouterModelId, bool Enabled);
