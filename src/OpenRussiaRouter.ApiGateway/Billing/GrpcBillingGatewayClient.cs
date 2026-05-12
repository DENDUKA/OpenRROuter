using System.Globalization;
using Grpc.Core;
using OpenRussiaRouter.ApiGateway.Billing.Interfaces;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.ApiGateway.Billing;

/// <summary>
/// Billing Gateway client backed by Billing Service gRPC API.
/// </summary>
public sealed class GrpcBillingGatewayClient(BillingGrpc.BillingServiceClient billingClient) : IBillingGatewayClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<GatewayPublicModel>> ListPublicModelsAsync(CancellationToken cancellationToken)
    {
        var response = await billingClient.ListPublicModelsAsync(
            new ListPublicModelsRequest(),
            cancellationToken: cancellationToken);

        return response.Models
            .Select(model => new GatewayPublicModel(model.Id, model.DisplayName))
            .ToArray();
    }

    /// <inheritdoc />
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

            return new GatewayBalanceCheckResult(
                response.Allowed,
                response.ModelId,
                string.IsNullOrWhiteSpace(response.PriceVersion) ? null : response.PriceVersion,
                MoneyAmount.Rub(ParseDecimal(response.BalanceRub)),
                string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode);
        }
        catch (RpcException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GatewayBalanceCheckResult(false, modelId, null, MoneyAmount.Rub(0), ErrorCodes.InternalError);
        }
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    /// <inheritdoc />
    public async Task<bool> RecordUsageAsync(GatewayUsageEvent usageEvent, CancellationToken cancellationToken)
    {
        var response = await billingClient.RecordUsageAsync(
            new RecordUsageRequest
            {
                RequestId = usageEvent.RequestId,
                UserId = usageEvent.UserId,
                ApiKeyId = usageEvent.ApiKeyId,
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
                CompletedAtUtc = usageEvent.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken);

        return response.Accepted;
    }
}
