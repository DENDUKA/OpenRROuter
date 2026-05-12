using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record BalanceInfo(decimal Amount);

public sealed record ModelInfo(string Id, string DisplayName, string UpstreamModelId, string PriceVersion, decimal InputPrice, decimal OutputPrice, decimal CachePrice, bool Enabled);

public sealed record UsageInfo(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ModelId,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostRub,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record AnomalyInfo(string Id, string RequestId, string Type, string Message, DateTimeOffset CreatedAt);

public sealed class BillingException(string ErrorCode) : Exception
{
    public string ErrorCode { get; } = ErrorCode;
}

public sealed class BillingClient(BillingGrpc.BillingServiceClient client)
{
    public async Task<BalanceInfo> GetBalanceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetBalanceAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetBalanceRequest { UserId = userId }, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new BillingException(response.ErrorCode);
        }
        return new BalanceInfo(decimal.Parse(response.BalanceRub, System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<UsageInfo>> ListUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.ListUsageAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListUsageRequest { UserId = userId }, cancellationToken: cancellationToken);
        return response.Records.Select(r => new UsageInfo(
            r.RequestId,
            r.UserId,
            r.ApiKeyId,
            r.ModelId,
            r.Usage?.InputTokens ?? 0,
            r.Usage?.OutputTokens ?? 0,
            r.Usage?.CacheTokens ?? 0,
            decimal.Parse(r.CostRub, System.Globalization.CultureInfo.InvariantCulture),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }

    public async Task CreateVirtualTopUpAsync(string adminId, string userId, decimal amountRub, string reason, CancellationToken cancellationToken = default)
    {
        var response = await client.CreateVirtualTopUpAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.CreateVirtualTopUpRequest
            {
                AdminId = adminId,
                UserId = userId,
                AmountRub = amountRub.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Reason = reason,
                IdempotencyKey = Guid.NewGuid().ToString()
            }, cancellationToken: cancellationToken);
        if (!response.Created)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListPublicModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListPublicModelsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListPublicModelsRequest(), cancellationToken: cancellationToken);
        return response.Models.Select(m => new ModelInfo(m.Id, m.DisplayName, "", "", 0m, 0m, 0m, true)).ToList();
    }

    public async Task<IReadOnlyList<AnomalyInfo>> ListAnomaliesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListBillingAnomaliesAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListBillingAnomaliesRequest(), cancellationToken: cancellationToken);
        return response.Anomalies.Select(a => new AnomalyInfo(
            a.Id,
            a.RequestId,
            a.Type,
            a.Message,
            DateTimeOffset.Parse(a.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }
}