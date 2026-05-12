using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

/// <summary>
/// Persisted append-only ledger entry row.
/// </summary>
public sealed class LedgerEntryEntity
{
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public LedgerEntryType Type { get; set; }

    public decimal Amount { get; set; }

    public Currency Currency { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Persisted active model price row.
/// </summary>
public sealed class ModelPriceEntity
{
    public string ModelId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string RouterModelId { get; set; } = string.Empty;

    public string PriceVersion { get; set; } = string.Empty;

    public decimal InputPerMillionAmount { get; set; }

    public decimal OutputPerMillionAmount { get; set; }

    public decimal CachePerMillionAmount { get; set; }

    public Currency Currency { get; set; }

    public bool Enabled { get; set; }
}

/// <summary>
/// Persisted usage record row.
/// </summary>
public sealed class UsageRecordEntity
{
    public string RequestId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ApiKeyId { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string PriceVersion { get; set; } = string.Empty;

    public string RouterNodeId { get; set; } = string.Empty;

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CacheTokens { get; set; }

    public decimal CostAmount { get; set; }

    public Currency CostCurrency { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Persisted billing anomaly row.
/// </summary>
public sealed class BillingAnomalyEntity
{
    public string Id { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
