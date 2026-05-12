using System.Globalization;
using Grpc.Core;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.BillingService.Application;

/// <summary>
/// gRPC Billing API used by Gateway and internal UI backends.
/// </summary>
public sealed class GrpcBillingService(BillingApplicationService billing) : BillingGrpc.BillingServiceBase
{
    /// <inheritdoc />
    public override Task<CheckBalanceAndModelResponse> CheckBalanceAndModel(
        CheckBalanceAndModelRequest request,
        ServerCallContext context)
    {
        var result = billing.CheckBalanceAndModel(request.UserId, request.ModelId);

        return Task.FromResult(new CheckBalanceAndModelResponse
        {
            Allowed = result.Allowed,
            ModelId = result.ModelId,
            PriceVersion = result.PriceVersion ?? string.Empty,
            BalanceRub = result.CurrentBalance.Amount.ToString(CultureInfo.InvariantCulture),
            ErrorCode = result.ErrorCode ?? string.Empty
        });
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override Task<RecordUsageResponse> RecordUsage(
        RecordUsageRequest request,
        ServerCallContext context)
    {
        var usageEvent = new UsageEvent(
            request.RequestId,
            request.UserId,
            request.ApiKeyId,
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

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTimeOffset.UtcNow;
    }
}
