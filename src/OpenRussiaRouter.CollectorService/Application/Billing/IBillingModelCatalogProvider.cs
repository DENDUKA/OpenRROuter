namespace OpenRussiaRouter.CollectorService.Application.Billing;

public interface IBillingModelCatalogProvider
{
    /// <summary>Весь каталог моделей биллинга, включая выключенные (<see cref="BillingCatalogModel.Enabled"/>).</summary>
    Task<IReadOnlyList<BillingCatalogModel>> ListModels(CancellationToken cancellationToken);
}
