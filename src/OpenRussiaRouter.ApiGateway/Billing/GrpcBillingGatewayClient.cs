using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class GrpcBillingGatewayClient(
    BillingGrpc.BillingServiceClient billingClient,
    ILogger<GrpcBillingGatewayClient> logger) : IBillingGatewayClient
{

    public async Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken)
    {
        var response = await billingClient.ListPublicModelsAsync(
            new ListPublicModelsRequest(),
            cancellationToken: cancellationToken);

        return response.Models
            .Select(model => new GatewayPublicModel(model.Id, model.DisplayName))
            .ToArray();
    }

    public async Task<GatewayBalanceCheckResult> CheckBalanceAndModelAsync(
        string userId,
        string modelId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await billingClient.CheckBalanceAndModelAsync(
                new CheckBalanceAndModelRequest
                {
                    UserId = userId,
                    ModelId = modelId
                },
                cancellationToken: cancellationToken);

            var routerModelId = string.IsNullOrWhiteSpace(response.RouterModelId)
                ? response.ModelId
                : response.RouterModelId;

            return new GatewayBalanceCheckResult(
                response.Allowed,
                response.ModelId,
                routerModelId,
                string.IsNullOrWhiteSpace(response.PriceVersion) ? null : response.PriceVersion,
                MoneyAmount.Rub(ParseDecimal(response.BalanceRub)),
                string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode);
        }
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Billing service balance check failed. UserId: {UserId}; ModelId: {ModelId}; RpcStatus: {RpcStatus}", userId, modelId, ex.StatusCode);
            return new GatewayBalanceCheckResult(false, modelId, modelId, null, MoneyAmount.Rub(0), ErrorCodes.InternalError);
        }
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    public async Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
    {
        var response = await billingClient.RecordUsageAsync(
            new RecordUsageRequest
            {
                RequestId = usageEvent.RequestId,
                UserId = usageEvent.UserId,
                ApiKeyId = usageEvent.ApiKeyId,
                ApiKeyPrefix = usageEvent.ApiKeyPrefix,
                ModelId = usageEvent.ModelId,
                RouterNodeId = usageEvent.RouterNodeId,
                Usage = new TokenUsage
                {
                    InputTokens = usageEvent.Usage.InputTokens,
                    OutputTokens = usageEvent.Usage.OutputTokens,
                    CacheTokens = usageEvent.Usage.CacheTokens
                },
                Status = usageEvent.Status,
                StartedAtUtc = usageEvent.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                CompletedAtUtc = usageEvent.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ProviderName = usageEvent.ProviderName ?? "",
                ProviderAccount = usageEvent.ProviderAccount ?? ""
            },
            cancellationToken: cancellationToken);

        return response.Accepted;
    }
}
