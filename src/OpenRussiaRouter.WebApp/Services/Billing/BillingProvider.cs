using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingTokenUsage = OpenRussiaRouter.Contracts.Grpc.Billing.V1.TokenUsage;

namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed class BillingProvider(BillingGrpc.BillingServiceClient client)
{
    public async Task<BalanceInfo> GetBalanceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetBalanceAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetBalanceRequest { UserId = userId }, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new BillingException(response.ErrorCode);
        }
        return new BalanceInfo(decimal.Parse(response.BalanceUsd, System.Globalization.CultureInfo.InvariantCulture));
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
            decimal.Parse(r.CostUsd, System.Globalization.CultureInfo.InvariantCulture),
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
            string.IsNullOrEmpty(r.CostUsd) ? 0m : decimal.Parse(r.CostUsd, CultureInfo.InvariantCulture),
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
            string.IsNullOrEmpty(response.TotalCostUsd) ? 0m : decimal.Parse(response.TotalCostUsd, CultureInfo.InvariantCulture),
            response.Models.Select(m => new ActivityModelSummaryInfo(
                m.ModelId, m.Requests, m.InputTokens, m.OutputTokens, m.CacheTokens,
                string.IsNullOrEmpty(m.CostUsd) ? 0m : decimal.Parse(m.CostUsd, CultureInfo.InvariantCulture))).ToList(),
            response.Keys.Select(k => new ActivityKeySummaryInfo(
                k.ApiKeyId, k.ApiKeyPrefix, k.Requests, k.InputTokens, k.OutputTokens, k.CacheTokens,
                string.IsNullOrEmpty(k.CostUsd) ? 0m : decimal.Parse(k.CostUsd, CultureInfo.InvariantCulture))).ToList());
    }

    public async Task<IReadOnlyList<TopUpInfo>> ListTopUpsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.ListTopUpsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListTopUpsRequest { UserId = userId },
            cancellationToken: cancellationToken);
        return response.Records.Select(r => new TopUpInfo(
            decimal.Parse(r.AmountUsd, System.Globalization.CultureInfo.InvariantCulture),
            r.Reason,
            DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }

    public async Task CreateVirtualTopUpAsync(string adminId, string userId, decimal amountUsd, string reason, CancellationToken cancellationToken = default)
    {
        var response = await client.CreateVirtualTopUpAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.CreateVirtualTopUpRequest
            {
                AdminId = adminId,
                UserId = userId,
                AmountUsd = amountUsd.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
        return response.Models.Select(m => new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            "",
            string.IsNullOrEmpty(m.InputPricePer1MUsd) ? 0m : decimal.Parse(m.InputPricePer1MUsd, CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(m.OutputPricePer1MUsd) ? 0m : decimal.Parse(m.OutputPricePer1MUsd, CultureInfo.InvariantCulture),
            0m,
            true,
            Description: string.IsNullOrEmpty(m.Description) ? null : m.Description,
            ContextLength: m.ContextLength == 0 ? null : (int?)m.ContextLength,
            Modalities: string.IsNullOrEmpty(m.Modalities) ? null : m.Modalities)).ToList();
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListModelsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListModelsRequest(), cancellationToken: cancellationToken);
        return response.Models.Select(m => new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            m.PriceVersion,
            decimal.Parse(m.InputPricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.OutputPricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.CachePricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            m.Enabled,
            Description: string.IsNullOrEmpty(m.Description) ? null : m.Description,
            ContextLength: m.ContextLength == 0 ? null : (int?)m.ContextLength,
            Modalities: string.IsNullOrEmpty(m.Modalities) ? null : m.Modalities,
            DescriptionSource: string.IsNullOrEmpty(m.DescriptionSource) ? null : m.DescriptionSource)).ToList();
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
                InputPricePer1MUsd = model.InputPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OutputPricePer1MUsd = model.OutputPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CachePricePer1MUsd = model.CachePrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Enabled = model.Enabled,
                Description = model.Description ?? string.Empty,
                ContextLength = model.ContextLength ?? 0,
                Modalities = model.Modalities ?? string.Empty,
                DescriptionSource = model.DescriptionSource ?? string.Empty,
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

    public async Task<(IReadOnlyList<AdminRequestInfo> Items, bool HasMore, string? NextCursor)> ListAdminRequestsAsync(
        string? afterCursor = null,
        int pageSize = 50,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListAdminRequestsRequest
        {
            AfterCursor = afterCursor ?? string.Empty,
            PageSize = pageSize,
            ModelFilter = modelFilter ?? string.Empty,
            ProviderFilter = providerFilter ?? string.Empty,
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };
        var response = await client.ListAdminRequestsAsync(request, cancellationToken: cancellationToken);

        var items = response.Records.Select(r => new AdminRequestInfo(
            r.RequestId,
            r.UserId,
            r.ApiKeyPrefix,
            r.ModelId,
            r.RouterNodeId,
            string.IsNullOrEmpty(r.ProviderName) ? null : r.ProviderName,
            string.IsNullOrEmpty(r.ProviderAccount) ? null : r.ProviderAccount,
            r.InputTokens,
            r.OutputTokens,
            r.CacheTokens,
            string.IsNullOrEmpty(r.CostUsd) ? 0m : decimal.Parse(r.CostUsd, CultureInfo.InvariantCulture),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(r.CompletedAtUtc, CultureInfo.InvariantCulture))).ToList();

        return (items, response.Records.Count >= pageSize && !string.IsNullOrEmpty(response.NextCursor),
            string.IsNullOrEmpty(response.NextCursor) ? null : response.NextCursor);
    }
}
