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

    public async Task<(IReadOnlyList<ActivityByTokenInfo> Items, bool HasMore, string? NextCursor)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        DateTimeOffset? afterCreatedAtUtc = null,
        int pageSize = 0,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
            return ([], false, null);

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListActivityByTokenRequest
        {
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ModelIdFilter = modelIdFilter ?? string.Empty,
            ApiKeyIdFilter = apiKeyIdFilter ?? string.Empty,
            AfterCreatedAtUtc = afterCreatedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            PageSize = pageSize
        };
        request.ApiKeyIds.AddRange(apiKeyIds);
        var response = await client.ListActivityByTokenAsync(request, cancellationToken: cancellationToken);

        var items = response.Records.Select(r => new ActivityByTokenInfo(
            r.RequestId,
            r.ApiKeyId,
            r.ApiKeyPrefix,
            r.ModelId,
            r.RouterNodeId,
            r.Usage?.InputTokens ?? 0,
            r.Usage?.OutputTokens ?? 0,
            r.Usage?.CacheTokens ?? 0,
            string.IsNullOrEmpty(r.CostRub) ? 0m : decimal.Parse(r.CostRub, CultureInfo.InvariantCulture),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(r.CompletedAtUtc, CultureInfo.InvariantCulture))).ToList();

        return (items, response.HasMore, string.IsNullOrEmpty(response.NextCursor) ? null : response.NextCursor);
    }

    public async Task<ActivitySummaryInfo> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
            return new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []);

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetActivitySummaryRequest
        {
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };
        request.ApiKeyIds.AddRange(apiKeyIds);
        var response = await client.GetActivitySummaryAsync(request, cancellationToken: cancellationToken);

        return new ActivitySummaryInfo(
            response.TotalRequests,
            response.TotalInputTokens,
            response.TotalOutputTokens,
            response.TotalCacheTokens,
            string.IsNullOrEmpty(response.TotalCostRub) ? 0m : decimal.Parse(response.TotalCostRub, CultureInfo.InvariantCulture),
            response.Models.Select(m => new ActivityModelSummaryInfo(
                m.ModelId, m.Requests, m.InputTokens, m.OutputTokens, m.CacheTokens,
                string.IsNullOrEmpty(m.CostRub) ? 0m : decimal.Parse(m.CostRub, CultureInfo.InvariantCulture))).ToList(),
            response.Keys.Select(k => new ActivityKeySummaryInfo(
                k.ApiKeyId, k.ApiKeyPrefix, k.Requests, k.InputTokens, k.OutputTokens, k.CacheTokens,
                string.IsNullOrEmpty(k.CostRub) ? 0m : decimal.Parse(k.CostRub, CultureInfo.InvariantCulture))).ToList());
    }

    public async Task<IReadOnlyList<TopUpInfo>> ListTopUpsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.ListTopUpsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListTopUpsRequest { UserId = userId },
            cancellationToken: cancellationToken);
        return response.Records.Select(r => new TopUpInfo(
            decimal.Parse(r.AmountRub, System.Globalization.CultureInfo.InvariantCulture),
            r.Reason,
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
