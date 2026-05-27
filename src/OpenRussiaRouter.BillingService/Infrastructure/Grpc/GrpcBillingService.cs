using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.BillingService.Infrastructure.Grpc;

public sealed class GrpcBillingService(
    BillingApplicationService billing,
    ILogger<GrpcBillingService> logger) : BillingGrpc.BillingServiceBase
{
    public override async Task<CheckBalanceAndModelResponse> CheckBalanceAndModel(
        CheckBalanceAndModelRequest request,
        ServerCallContext context)
    {
        var result = await billing.CheckBalanceAndModelAsync(request.UserId, request.ModelId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new CheckBalanceAndModelResponse
        {
            Allowed = result.Allowed,
            ModelId = result.ModelId,
            RouterModelId = result.RouterModelId,
            PriceVersion = result.PriceVersion ?? string.Empty,
            BalanceRub = result.CurrentBalance.Amount.ToString(CultureInfo.InvariantCulture),
            ErrorCode = result.ErrorCode ?? string.Empty
        };
    }

    public override async Task<ListPublicModelsResponse> ListPublicModels(
        ListPublicModelsRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var publicModels = await billing.ListPublicModelsAsync(ct).ConfigureAwait(false);
        var pricesByRouterId = (await billing.ListAllModelsAsync(ct).ConfigureAwait(false))
            .ToDictionary(model => model.RouterModelId, StringComparer.Ordinal);

        var response = new ListPublicModelsResponse();
        response.Models.AddRange(
            publicModels.Select(model =>
            {
                pricesByRouterId.TryGetValue(model.Id, out var price);

                return new Contracts.Grpc.Billing.V1.PublicModel
                {
                    Id = model.Id,
                    DisplayName = model.DisplayName,
                    UpstreamModelId = price?.RouterModelId ?? string.Empty,
                    InputPricePer1MRub = price?.Price.InputPerMillion.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    OutputPricePer1MRub = price?.Price.OutputPerMillion.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                };
            }));
        return response;
    }

    public override async Task<RecordUsageResponse> RecordUsage(
        RecordUsageRequest request,
        ServerCallContext context)
    {
        var usageEvent = new UsageEvent(
            request.RequestId,
            request.UserId,
            request.ApiKeyId,
            request.ApiKeyPrefix,
            request.ModelId,
            request.RouterNodeId,
            new BuildingBlocks.Usage.TokenUsage(
                request.Usage?.InputTokens ?? 0,
                request.Usage?.OutputTokens ?? 0,
                request.Usage?.CacheTokens ?? 0),
            request.Status,
            ParseTimestamp(request.StartedAtUtc),
            ParseTimestamp(request.CompletedAtUtc),
            string.IsNullOrEmpty(request.ProviderName) ? null : request.ProviderName,
            string.IsNullOrEmpty(request.ProviderAccount) ? null : request.ProviderAccount);

        var result = await billing.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new RecordUsageResponse
        {
            Accepted = result.Accepted,
            RequestId = result.RequestId ?? string.Empty,
            ErrorCode = result.ErrorCode ?? string.Empty
        };
    }

    public override async Task<GetBalanceResponse> GetBalance(
        GetBalanceRequest request,
        ServerCallContext context)
    {
        try
        {
            var balance = await billing.GetBalanceAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new GetBalanceResponse
            {
                UserId = request.UserId,
                BalanceRub = balance.Amount.ToString(CultureInfo.InvariantCulture)
            };
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "GetBalance failed. UserId: {UserId}", request.UserId);
            return new GetBalanceResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<CreateVirtualTopUpResponse> CreateVirtualTopUp(
        CreateVirtualTopUpRequest request,
        ServerCallContext context)
    {
        try
        {
            var amount = MoneyAmount.Rub(decimal.Parse(request.AmountRub, CultureInfo.InvariantCulture));
            var entry = await billing.CreateVirtualTopUpAsync(
                request.AdminId,
                request.UserId,
                amount,
                request.Reason,
                request.IdempotencyKey,
                DateTimeOffset.UtcNow,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            var balance = await billing.GetBalanceAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new CreateVirtualTopUpResponse
            {
                Created = true,
                LedgerEntryId = entry.Id,
                UserId = entry.UserId,
                AmountRub = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                BalanceRub = balance.Amount.ToString(CultureInfo.InvariantCulture)
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger.LogWarning(ex, "CreateVirtualTopUp failed. AdminId: {AdminId}; UserId: {UserId}; IdempotencyKey: {IdempotencyKey}", request.AdminId, request.UserId, request.IdempotencyKey);
            return new CreateVirtualTopUpResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<UpsertModelPriceResponse> UpsertModelPrice(
        UpsertModelPriceRequest request,
        ServerCallContext context)
    {
        try
        {
            await billing.UpsertModelAsync(new ModelPrice(
                request.ModelId,
                request.DisplayName,
                request.UpstreamModelId,
                request.PriceVersion,
                new ModelTokenPrice(
                    MoneyAmount.Rub(decimal.Parse(request.InputPricePer1MRub, CultureInfo.InvariantCulture)),
                    MoneyAmount.Rub(decimal.Parse(request.OutputPricePer1MRub, CultureInfo.InvariantCulture)),
                    MoneyAmount.Rub(decimal.Parse(request.CachePricePer1MRub, CultureInfo.InvariantCulture))),
                request.Enabled),
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new UpsertModelPriceResponse
            {
                Upserted = true,
                ModelId = request.ModelId
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger.LogWarning(ex, "UpsertModelPrice failed. ModelId: {ModelId}; PriceVersion: {PriceVersion}", request.ModelId, request.PriceVersion);
            return new UpsertModelPriceResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<ListBillingAnomaliesResponse> ListBillingAnomalies(
        ListBillingAnomaliesRequest request,
        ServerCallContext context)
    {
        var response = new ListBillingAnomaliesResponse();
        response.Anomalies.AddRange(
            (await billing.ListAnomaliesAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(anomaly => new Contracts.Grpc.Billing.V1.BillingAnomaly
            {
                Id = anomaly.Id,
                RequestId = anomaly.RequestId,
                Type = anomaly.Type,
                Message = anomaly.Message,
                CreatedAtUtc = anomaly.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }));
        return response;
    }

    public override async Task<ListModelsResponse> ListModels(
        ListModelsRequest request,
        ServerCallContext context)
    {
        var response = new ListModelsResponse();
        response.Models.AddRange(
            (await billing.ListAllModelsAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(model => new Contracts.Grpc.Billing.V1.ModelDetail
            {
                Id = model.ModelId,
                DisplayName = model.DisplayName,
                UpstreamModelId = model.RouterModelId,
                PriceVersion = model.PriceVersion,
                InputPricePer1MRub = model.Price.InputPerMillion.Amount.ToString(CultureInfo.InvariantCulture),
                OutputPricePer1MRub = model.Price.OutputPerMillion.Amount.ToString(CultureInfo.InvariantCulture),
                CachePricePer1MRub = model.Price.CachePerMillion.Amount.ToString(CultureInfo.InvariantCulture),
                Enabled = model.Enabled
            }));
        return response;
    }

    public override async Task<ListUsageResponse> ListUsage(
        ListUsageRequest request,
        ServerCallContext context)
    {
        var response = new ListUsageResponse();
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId;
        response.Records.AddRange(
            (await billing.ListUsageAsync(userId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(usage => new Contracts.Grpc.Billing.V1.UsageRecord
            {
                RequestId = usage.RequestId,
                UserId = usage.UserId,
                ApiKeyId = usage.ApiKeyId,
                ModelId = usage.ModelId,
                PriceVersion = usage.PriceVersion,
                RouterNodeId = usage.RouterNodeId,
                Usage = new Contracts.Grpc.Billing.V1.TokenUsage
                {
                    InputTokens = usage.Usage.InputTokens,
                    OutputTokens = usage.Usage.OutputTokens,
                    CacheTokens = usage.Usage.CacheTokens
                },
                CostRub = usage.Cost.Amount.ToString(CultureInfo.InvariantCulture),
                Status = usage.Status,
                CreatedAtUtc = usage.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }));
        return response;
    }

    public override async Task<ListActivityByTokenResponse> ListActivityByToken(
        ListActivityByTokenRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var (items, hasMore) = await billing.ListActivityByTokenAsync(
            request.ApiKeyIds.ToArray(),
            fromUtc: ParseOptionalTimestamp(request.FromUtc),
            toUtc: ParseOptionalTimestamp(request.ToUtc),
            modelIdFilter: string.IsNullOrEmpty(request.ModelIdFilter) ? null : request.ModelIdFilter,
            apiKeyIdFilter: string.IsNullOrEmpty(request.ApiKeyIdFilter) ? null : request.ApiKeyIdFilter,
            afterCreatedAtUtc: ParseOptionalTimestamp(request.AfterCreatedAtUtc),
            pageSize: request.PageSize > 0 ? request.PageSize : null,
            ct: ct).ConfigureAwait(false);

        var response = new ListActivityByTokenResponse { HasMore = hasMore };
        if (hasMore && items.Count > 0)
            response.NextCursor = items.Last().CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture);

        response.Records.AddRange(items.Select(activity => new Contracts.Grpc.Billing.V1.ActivityByTokenRecord
        {
            RequestId = activity.RequestId,
            ApiKeyId = activity.ApiKeyId,
            ApiKeyPrefix = activity.ApiKeyPrefix,
            ModelId = activity.ModelId,
            RouterNodeId = activity.RouterNodeId,
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage
            {
                InputTokens = activity.Usage.InputTokens,
                OutputTokens = activity.Usage.OutputTokens,
                CacheTokens = activity.Usage.CacheTokens
            },
            Status = activity.Status,
            CreatedAtUtc = activity.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CompletedAtUtc = activity.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CostRub = activity.Cost.Amount.ToString(CultureInfo.InvariantCulture),
            ProviderName = activity.ProviderName ?? "",
            ProviderAccount = activity.ProviderAccount ?? ""
        }));
        return response;
    }

    public override async Task<GetActivitySummaryResponse> GetActivitySummary(
        GetActivitySummaryRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var summary = await billing.GetActivitySummaryAsync(
            request.ApiKeyIds.ToArray(),
            fromUtc: ParseOptionalTimestamp(request.FromUtc),
            toUtc: ParseOptionalTimestamp(request.ToUtc),
            ct: ct).ConfigureAwait(false);

        var response = new GetActivitySummaryResponse
        {
            TotalRequests = summary.TotalRequests,
            TotalInputTokens = summary.TotalInputTokens,
            TotalOutputTokens = summary.TotalOutputTokens,
            TotalCacheTokens = summary.TotalCacheTokens,
            TotalCostRub = summary.TotalCost.Amount.ToString(CultureInfo.InvariantCulture)
        };
        response.Models.AddRange(summary.Models.Select(m => new Contracts.Grpc.Billing.V1.ActivityModelSummary
        {
            ModelId = m.ModelId,
            Requests = m.Requests,
            InputTokens = m.InputTokens,
            OutputTokens = m.OutputTokens,
            CacheTokens = m.CacheTokens,
            CostRub = m.Cost.Amount.ToString(CultureInfo.InvariantCulture)
        }));
        response.Keys.AddRange(summary.Keys.Select(k => new Contracts.Grpc.Billing.V1.ActivityKeySummary
        {
            ApiKeyId = k.ApiKeyId,
            ApiKeyPrefix = k.ApiKeyPrefix,
            Requests = k.Requests,
            InputTokens = k.InputTokens,
            OutputTokens = k.OutputTokens,
            CacheTokens = k.CacheTokens,
            CostRub = k.Cost.Amount.ToString(CultureInfo.InvariantCulture)
        }));
        return response;
    }

    public override async Task<ListTopUpsResponse> ListTopUps(
        ListTopUpsRequest request,
        ServerCallContext context)
    {
        var response = new ListTopUpsResponse();
        response.Records.AddRange(
            (await billing.ListTopUpsAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(entry => new TopUpRecord
            {
                Id = entry.Id,
                AmountRub = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                Reason = entry.Reason,
                CreatedAtUtc = entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }));
        return response;
    }

    public override async Task<ListAdminRequestsResponse> ListAdminRequests(
        ListAdminRequestsRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var (items, hasMore) = await billing.GetAdminUsageRecordsAsync(
            pageSize,
            afterCursor: string.IsNullOrEmpty(request.AfterCursor) ? null : request.AfterCursor,
            modelFilter: string.IsNullOrEmpty(request.ModelFilter) ? null : request.ModelFilter,
            providerFilter: string.IsNullOrEmpty(request.ProviderFilter) ? null : request.ProviderFilter,
            fromUtc: ParseOptionalTimestamp(request.FromUtc),
            toUtc: ParseOptionalTimestamp(request.ToUtc),
            ct: ct).ConfigureAwait(false);

        var response = new ListAdminRequestsResponse();
        if (hasMore && items.Count > 0)
            response.NextCursor = items.Last().CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture);

        response.Records.AddRange(items.Select(r => new Contracts.Grpc.Billing.V1.AdminRequestRecord
        {
            RequestId = r.RequestId,
            UserId = r.UserId,
            ApiKeyPrefix = r.ApiKeyPrefix,
            ModelId = r.ModelId,
            RouterNodeId = r.RouterNodeId,
            ProviderName = r.ProviderName ?? "",
            ProviderAccount = r.ProviderAccount ?? "",
            InputTokens = r.Usage.InputTokens,
            OutputTokens = r.Usage.OutputTokens,
            CacheTokens = r.Usage.CacheTokens,
            CostRub = r.Cost.Amount.ToString(CultureInfo.InvariantCulture),
            Status = r.Status,
            CreatedAtUtc = r.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CompletedAtUtc = r.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }));
        return response;
    }

    private DateTimeOffset ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();

        logger.LogWarning("Failed to parse timestamp '{Value}'; using UtcNow as fallback.", value);
        return DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? ParseOptionalTimestamp(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
