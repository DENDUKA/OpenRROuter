namespace OpenRussiaRouter.CollectorService.Application.Billing;

public interface IActiveBillingModelProvider
{
    Task<IReadOnlyList<ActiveBillingModel>> ListActiveModelsAsync(CancellationToken cancellationToken);
}
