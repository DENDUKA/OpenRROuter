using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

/// <summary>
/// Persisted model usage event and calculated cost.
/// </summary>
/// <param name="RequestId">Request id used for idempotency.</param>
/// <param name="UserId">User id.</param>
/// <param name="ApiKeyId">API key id.</param>
/// <param name="ModelId">Public model id.</param>
/// <param name="PriceVersion">Pricing version used for calculation.</param>
/// <param name="RouterNodeId">9Router node id.</param>
/// <param name="Usage">Token usage.</param>
/// <param name="Cost">Calculated cost.</param>
/// <param name="Status">Final request status.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
public sealed record UsageRecord(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ModelId,
    string PriceVersion,
    string RouterNodeId,
    TokenUsage Usage,
    MoneyAmount Cost,
    string Status,
    DateTimeOffset CreatedAtUtc);

