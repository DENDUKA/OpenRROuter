namespace OpenRussiaRouter.CollectorService.Application.NodeAdmin;

public interface INodeAdminClient
{
    string DescribeListProvidersRequest(Uri endpoint);
    string DescribeListModelsRequest(Uri endpoint);
    string DescribeListCombosRequest(Uri endpoint);

    Task<NodeProvidersWithModelsResponse> ListProvidersWithModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken);

    Task<NodeModelsResponse> ListModelsAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken);

    Task<NodeCombosResponse> ListCombosAsync(
        Uri endpoint, string controlToken, CancellationToken cancellationToken);

    Task<NodeProviderQuotaResponse> GetProviderQuotaAsync(
        Uri endpoint, string controlToken, bool includeInactive, CancellationToken cancellationToken);
}
