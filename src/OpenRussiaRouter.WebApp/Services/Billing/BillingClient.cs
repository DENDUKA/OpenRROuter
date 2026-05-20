using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingTokenUsage = OpenRussiaRouter.Contracts.Grpc.Billing.V1.TokenUsage;

namespace OpenRussiaRouter.WebApp.Services.Billing;

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

    public async Task<string?> RecordUsageAsync(
        string requestId,
        string userId,
        string apiKeyId,
        string apiKeyPrefix,
        string modelId,
        string routerNodeId,
        long inputTokens,
        long outputTokens,
        long cacheTokens,
        string status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var response = await client.RecordUsageAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.RecordUsageRequest
            {
                RequestId = requestId,
                UserId = userId,
                ApiKeyId = apiKeyId,
                ApiKeyPrefix = apiKeyPrefix,
                ModelId = modelId,
                RouterNodeId = routerNodeId,
                Usage = new BillingTokenUsage
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    CacheTokens = cacheTokens
                },
                Status = status,
                StartedAtUtc = startedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                CompletedAtUtc = completedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken);

        return response.Accepted ? null : response.ErrorCode;
    }

    public async Task<IReadOnlyList<ActivityByTokenInfo>> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
        {
            return [];
        }

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListActivityByTokenRequest();
        request.ApiKeyIds.AddRange(apiKeyIds);
        var response = await client.ListActivityByTokenAsync(request, cancellationToken: cancellationToken);

        return response.Records.Select(r => new ActivityByTokenInfo(
            r.RequestId,
            r.ApiKeyId,
            r.ApiKeyPrefix,
            r.ModelId,
            r.RouterNodeId,
            r.Usage?.InputTokens ?? 0,
            r.Usage?.OutputTokens ?? 0,
            r.Usage?.CacheTokens ?? 0,
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(r.CompletedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
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

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListModelsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListModelsRequest(), cancellationToken: cancellationToken);
        return response.Models.Select(m => new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            m.PriceVersion,
            decimal.Parse(m.InputPricePer1MRub, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.OutputPricePer1MRub, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.CachePricePer1MRub, System.Globalization.CultureInfo.InvariantCulture),
            m.Enabled)).ToList();
    }

    public async Task UpsertModelAsync(ModelInfo model, CancellationToken cancellationToken = default)
    {
        var response = await client.UpsertModelPriceAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertModelPriceRequest
            {
                ModelId = model.Id,
                DisplayName = model.DisplayName,
                UpstreamModelId = model.UpstreamModelId,
                PriceVersion = model.PriceVersion,
                InputPricePer1MRub = model.InputPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OutputPricePer1MRub = model.OutputPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CachePricePer1MRub = model.CachePrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Enabled = model.Enabled
            }, cancellationToken: cancellationToken);
        if (!response.Upserted)
        {
            throw new BillingException(response.ErrorCode);
        }
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
