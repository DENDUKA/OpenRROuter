namespace OpenRussiaRouter.ApiGateway.Billing;

public interface IBillingProvider
{

    Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken);

    Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(
        string userId,
        string modelId,
        bool adminBypassDisabled,
        CancellationToken cancellationToken);

    Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken);
}
