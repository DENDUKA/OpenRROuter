using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>
/// gRPC wrapper for usage tracking, activity, admin requests, and anomalies.
/// Extracted from <see cref="BillingProvider"/> (T-061).
/// </summary>
internal sealed class BillingUsageProvider(BillingGrpc.BillingServiceClient client)
{
    public async Task<(IReadOnlyList<UsageInfo> Items, bool HasMore, string? NextCursor)> ListUsageAsync(string userId, string? afterCursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.ListUsageAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListUsageRequest { UserId = userId, AfterCursor = afterCursor ?? string.Empty }, cancellationToken: cancellationToken);
        var items = response.Records.Select(r => new UsageInfo(
            r.RequestId,
            r.UserId,
            r.ApiKeyId,
            r.ModelId,
            r.Usage?.InputTokens ?? 0,
            r.Usage?.OutputTokens ?? 0,
            r.Usage?.CacheTokens ?? 0,
            ParseDecimal(r.CostUsd),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture))).ToList();
        return (items, response.HasMore, response.NextCursor);
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
                Usage = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.TokenUsage
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
        string? afterCursor = null,
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
            AfterCursor = afterCursor ?? string.Empty,
            PageSize = pageSize
        };
        request.ApiKeyIds.Add(apiKeyIds);

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
            ParseDecimal(r.CostUsd),
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
            ParseDecimal(response.TotalCostUsd),
            response.Models.Select(m => new ActivityModelSummaryInfo(
                m.ModelId, m.Requests, m.InputTokens, m.OutputTokens, m.CacheTokens,
                ParseDecimal(m.CostUsd))).ToList(),
            response.Keys.Select(k => new ActivityKeySummaryInfo(
                k.ApiKeyId, k.ApiKeyPrefix, k.Requests, k.InputTokens, k.OutputTokens, k.CacheTokens,
                ParseDecimal(k.CostUsd))).ToList());
    }

    public async Task<IReadOnlyList<AnomalyInfo>> ListAnomaliesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListBillingAnomaliesAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListBillingAnomaliesRequest(), cancellationToken: cancellationToken);
        return response.Anomalies.Select(a => new AnomalyInfo(
            a.Id,
            a.RequestId,
            a.Type,
            a.Message,
            DateTimeOffset.Parse(a.CreatedAtUtc, CultureInfo.InvariantCulture))).ToList();
    }

    public async Task<(IReadOnlyList<AdminRequestInfo> Items, bool HasMore, string? NextCursor)> ListAdminRequestsAsync(
        string? afterCursor = null,
        int pageSize = 50,
        IReadOnlyList<string>? modelFilters = null,
        IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null,
        IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListAdminRequestsRequest
        {
            AfterCursor = afterCursor ?? string.Empty,
            PageSize = pageSize,
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };
        if (modelFilters is { Count: > 0 }) request.ModelFilters.AddRange(modelFilters);
        if (providerFilters is { Count: > 0 }) request.ProviderFilters.AddRange(providerFilters);
        if (apiKeyFilters is { Count: > 0 }) request.ApiKeyFilters.AddRange(apiKeyFilters);
        if (nodeFilters is { Count: > 0 }) request.NodeFilters.AddRange(nodeFilters);
        if (accountFilters is { Count: > 0 }) request.AccountFilters.AddRange(accountFilters);

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
            ParseDecimal(r.CostUsd),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(r.CompletedAtUtc, CultureInfo.InvariantCulture))).ToList();

        return (items, response.Records.Count >= pageSize && !string.IsNullOrEmpty(response.NextCursor),
            string.IsNullOrEmpty(response.NextCursor) ? null : response.NextCursor);
    }

    public async Task<(long TotalRequests, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd)> GetAdminRequestsSummaryAsync(
        IReadOnlyList<string>? modelFilters = null,
        IReadOnlyList<string>? providerFilters = null,
        IReadOnlyList<string>? apiKeyFilters = null,
        IReadOnlyList<string>? nodeFilters = null,
        IReadOnlyList<string>? accountFilters = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetAdminRequestsSummaryRequest
        {
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };
        if (modelFilters is { Count: > 0 }) request.ModelFilters.AddRange(modelFilters);
        if (providerFilters is { Count: > 0 }) request.ProviderFilters.AddRange(providerFilters);
        if (apiKeyFilters is { Count: > 0 }) request.ApiKeyFilters.AddRange(apiKeyFilters);
        if (nodeFilters is { Count: > 0 }) request.NodeFilters.AddRange(nodeFilters);
        if (accountFilters is { Count: > 0 }) request.AccountFilters.AddRange(accountFilters);

        var response = await client.GetAdminRequestsSummaryAsync(request, cancellationToken: cancellationToken);
        return (
            response.TotalRequests,
            response.TotalInputTokens,
            response.TotalOutputTokens,
            response.TotalCacheTokens,
            ParseDecimal(response.TotalCostUsd));
    }

    private static decimal ParseDecimal(string value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, CultureInfo.InvariantCulture);
}
