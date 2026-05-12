namespace OpenRussiaRouter.ApiGateway.Billing.Interfaces;

/// <summary>
/// Billing operations needed by the public Gateway.
/// </summary>
public interface IBillingGatewayClient
{
    /// <summary>
    /// Lists models visible to API clients.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Public models.</returns>
    Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a user may start a request for a model.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="modelId">Public model id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Balance check result.</returns>
    Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(
        string userId,
        string modelId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records usage after a router response.
    /// </summary>
    /// <param name="usageEvent">Usage event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether Billing accepted the event.</returns>
    Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken);
}
