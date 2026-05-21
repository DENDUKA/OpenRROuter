using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class GrpcBillingService(
    BillingApplicationService billing,
    ILogger<GrpcBillingService>? logger = null) : BillingGrpc.BillingServiceBase
{

    public override Task<CheckBalanceAndModelResponse> CheckBalanceAndModel(
        CheckBalanceAndModelRequest request,
        ServerCallContext context)
    {
        var result = billing.CheckBalanceAndModel(request.UserId, request.ModelId);

        return Task.FromResult(new CheckBalanceAndModelResponse
        {
            Allowed = result.Allowed,
            ModelId = result.ModelId,
            RouterModelId = result.RouterModelId,
            PriceVersion = result.PriceVersion ?? string.Empty,
            BalanceRub = result.CurrentBalance.Amount.ToString(CultureInfo.InvariantCulture),
            ErrorCode = result.ErrorCode ?? string.Empty
        });
    }

    public override Task<ListPublicModelsResponse> ListPublicModels(
        ListPublicModelsRequest request,
        ServerCallContext context)
    {
        var response = new ListPublicModelsResponse();
        response.Models.AddRange(billing.ListPublicModels().Select(model => new Contracts.Grpc.Billing.V1.PublicModel
        {
            Id = model.Id,
            DisplayName = model.DisplayName
        }));

        return Task.FromResult(response);
    }

    public override Task<RecordUsageResponse> RecordUsage(
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
            ParseTimestamp(request.CompletedAtUtc));

        var result = billing.RecordUsage(usageEvent, DateTimeOffset.UtcNow);

        return Task.FromResult(new RecordUsageResponse
        {
            Accepted = result.Accepted,
            RequestId = result.RequestId ?? string.Empty,
            ErrorCode = result.ErrorCode ?? string.Empty
        });
    }

    public override Task<GetBalanceResponse> GetBalance(
        GetBalanceRequest request,
        ServerCallContext context)
    {
        try
        {
            var balance = billing.GetBalance(request.UserId);
            return Task.FromResult(new GetBalanceResponse
            {
                UserId = request.UserId,
                BalanceRub = balance.Amount.ToString(CultureInfo.InvariantCulture)
            });
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, "GetBalance failed. UserId: {UserId}", request.UserId);
            return Task.FromResult(new GetBalanceResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<CreateVirtualTopUpResponse> CreateVirtualTopUp(
        CreateVirtualTopUpRequest request,
        ServerCallContext context)
    {
        try
        {
            var amount = MoneyAmount.Rub(decimal.Parse(request.AmountRub, CultureInfo.InvariantCulture));
            var entry = billing.CreateVirtualTopUp(
                request.AdminId,
                request.UserId,
                amount,
                request.Reason,
                request.IdempotencyKey,
                DateTimeOffset.UtcNow);
            var balance = billing.GetBalance(request.UserId);

            return Task.FromResult(new CreateVirtualTopUpResponse
            {
                Created = true,
                LedgerEntryId = entry.Id,
                UserId = entry.UserId,
                AmountRub = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                BalanceRub = balance.Amount.ToString(CultureInfo.InvariantCulture)
            });
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger?.LogWarning(ex, "CreateVirtualTopUp failed. AdminId: {AdminId}; UserId: {UserId}; IdempotencyKey: {IdempotencyKey}", request.AdminId, request.UserId, request.IdempotencyKey);
            return Task.FromResult(new CreateVirtualTopUpResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<UpsertModelPriceResponse> UpsertModelPrice(
        UpsertModelPriceRequest request,
        ServerCallContext context)
    {
        try
        {
            billing.UpsertModel(new ModelPrice(
                request.ModelId,
                request.DisplayName,
                request.UpstreamModelId,
                request.PriceVersion,
                new ModelTokenPrice(
                    MoneyAmount.Rub(decimal.Parse(request.InputPricePer1MRub, CultureInfo.InvariantCulture)),
                    MoneyAmount.Rub(decimal.Parse(request.OutputPricePer1MRub, CultureInfo.InvariantCulture)),
                    MoneyAmount.Rub(decimal.Parse(request.CachePricePer1MRub, CultureInfo.InvariantCulture))),
                request.Enabled));

            return Task.FromResult(new UpsertModelPriceResponse
            {
                Upserted = true,
                ModelId = request.ModelId
            });
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger?.LogWarning(ex, "UpsertModelPrice failed. ModelId: {ModelId}; PriceVersion: {PriceVersion}", request.ModelId, request.PriceVersion);
            return Task.FromResult(new UpsertModelPriceResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<ListBillingAnomaliesResponse> ListBillingAnomalies(
        ListBillingAnomaliesRequest request,
        ServerCallContext context)
    {
        var response = new ListBillingAnomaliesResponse();
        response.Anomalies.AddRange(billing.ListAnomalies().Select(anomaly => new Contracts.Grpc.Billing.V1.BillingAnomaly
        {
            Id = anomaly.Id,
            RequestId = anomaly.RequestId,
            Type = anomaly.Type,
            Message = anomaly.Message,
            CreatedAtUtc = anomaly.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }));

return Task.FromResult(response);
    }

    public override Task<ListModelsResponse> ListModels(
        ListModelsRequest request,
        ServerCallContext context)
    {
        var response = new ListModelsResponse();
        response.Models.AddRange(billing.ListAllModels().Select(model => new Contracts.Grpc.Billing.V1.ModelDetail
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

return Task.FromResult(response);
    }

    public override Task<ListUsageResponse> ListUsage(
        ListUsageRequest request,
        ServerCallContext context)
    {
        var response = new ListUsageResponse();
        response.Records.AddRange(billing.ListUsage(string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId).Select(usage => new Contracts.Grpc.Billing.V1.UsageRecord
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

        return Task.FromResult(response);
    }

    public override Task<ListActivityByTokenResponse> ListActivityByToken(
        ListActivityByTokenRequest request,
        ServerCallContext context)
    {
        var response = new ListActivityByTokenResponse();
        response.Records.AddRange(billing.ListActivityByToken(request.ApiKeyIds.ToArray()).Select(activity => new Contracts.Grpc.Billing.V1.ActivityByTokenRecord
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
            CompletedAtUtc = activity.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }));

        return Task.FromResult(response);
    }

    public override Task<ListTopUpsResponse> ListTopUps(
        ListTopUpsRequest request,
        ServerCallContext context)
    {
        var response = new ListTopUpsResponse();
        response.Records.AddRange(billing.ListTopUps(request.UserId).Select(entry => new TopUpRecord
        {
            Id = entry.Id,
            AmountRub = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            Reason = entry.Reason,
            CreatedAtUtc = entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }));
        return Task.FromResult(response);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTimeOffset.UtcNow;
    }
}
