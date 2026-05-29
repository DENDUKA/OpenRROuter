using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public static class BillingPersistenceMapper
{
    public static LedgerEntryEntity ToEntity(LedgerEntry entry)
    {
        return new LedgerEntryEntity
        {
            Id = entry.Id,
            UserId = entry.UserId,
            Type = entry.Type,
            Amount = entry.Amount.Amount,
            Currency = entry.Amount.Currency,
            IdempotencyKey = entry.IdempotencyKey,
            Reason = entry.Reason,
            CreatedAtUtc = entry.CreatedAtUtc
        };
    }

    public static LedgerEntry ToDomain(LedgerEntryEntity entity)
    {
        return new LedgerEntry(
            entity.Id,
            entity.UserId,
            entity.Type,
            new MoneyAmount(entity.Amount, entity.Currency),
            entity.IdempotencyKey,
            entity.Reason,
            entity.CreatedAtUtc);
    }

    public static ModelPriceEntity ToEntity(ModelPrice model)
    {
        return new ModelPriceEntity
        {
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            RouterModelId = model.RouterModelId,
            PriceVersion = model.PriceVersion,
            InputPerMillionAmount = model.Price.InputPerMillion.Amount,
            OutputPerMillionAmount = model.Price.OutputPerMillion.Amount,
            CachePerMillionAmount = model.Price.CachePerMillion.Amount,
            Currency = model.Price.InputPerMillion.Currency,
            Enabled = model.Enabled,
            Description = model.Description,
            ContextLength = model.ContextLength,
            Modalities = model.Modalities,
            DescriptionSource = model.DescriptionSource
        };
    }

    public static ModelPrice ToDomain(ModelPriceEntity entity)
    {
        return new ModelPrice(
            entity.ModelId,
            entity.DisplayName,
            entity.RouterModelId,
            entity.PriceVersion,
            new ModelTokenPrice(
                new MoneyAmount(entity.InputPerMillionAmount, entity.Currency),
                new MoneyAmount(entity.OutputPerMillionAmount, entity.Currency),
                new MoneyAmount(entity.CachePerMillionAmount, entity.Currency)),
            entity.Enabled,
            Description: entity.Description,
            ContextLength: entity.ContextLength,
            Modalities: entity.Modalities,
            DescriptionSource: entity.DescriptionSource);
    }

    public static UsageRecordEntity ToEntity(UsageRecord record)
    {
        return new UsageRecordEntity
        {
            RequestId = record.RequestId,
            UserId = record.UserId,
            ApiKeyId = record.ApiKeyId,
            ModelId = record.ModelId,
            PriceVersion = record.PriceVersion,
            RouterNodeId = record.RouterNodeId,
            InputTokens = record.Usage.InputTokens,
            OutputTokens = record.Usage.OutputTokens,
            CacheTokens = record.Usage.CacheTokens,
            CostAmount = record.Cost.Amount,
            CostCurrency = record.Cost.Currency,
            Status = record.Status,
            CreatedAtUtc = record.CreatedAtUtc,
            ProviderName = record.ProviderName,
            ProviderAccount = record.ProviderAccount
        };
    }

    public static UsageRecord ToDomain(UsageRecordEntity entity)
    {
        return new UsageRecord(
            entity.RequestId,
            entity.UserId,
            entity.ApiKeyId,
            entity.ModelId,
            entity.PriceVersion,
            entity.RouterNodeId,
            new TokenUsage(entity.InputTokens, entity.OutputTokens, entity.CacheTokens),
            new MoneyAmount(entity.CostAmount, entity.CostCurrency),
            entity.Status,
            entity.CreatedAtUtc,
            entity.ProviderName,
            entity.ProviderAccount);
    }

    public static ActivityByTokenEntity ToEntity(ActivityByTokenRecord record)
    {
        return new ActivityByTokenEntity
        {
            RequestId = record.RequestId,
            ApiKeyId = record.ApiKeyId,
            ApiKeyPrefix = record.ApiKeyPrefix,
            ModelId = record.ModelId,
            RouterNodeId = record.RouterNodeId,
            InputTokens = record.Usage.InputTokens,
            OutputTokens = record.Usage.OutputTokens,
            CacheTokens = record.Usage.CacheTokens,
            Status = record.Status,
            CreatedAtUtc = record.CreatedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            ProviderName = record.ProviderName,
            ProviderAccount = record.ProviderAccount
        };
    }

    public static ActivityByTokenRecord ToDomain(ActivityByTokenEntity entity)
    {
        return new ActivityByTokenRecord(
            entity.RequestId,
            entity.ApiKeyId,
            entity.ApiKeyPrefix,
            entity.ModelId,
            entity.RouterNodeId,
            new TokenUsage(entity.InputTokens, entity.OutputTokens, entity.CacheTokens),
            new MoneyAmount(entity.CostAmount ?? 0m, Currency.Usd),
            entity.Status,
            entity.CreatedAtUtc,
            entity.CompletedAtUtc,
            entity.ProviderName,
            entity.ProviderAccount);
    }

    public static AdminRequestRecord ToDomain(AdminRequestEntity entity)
    {
        return new AdminRequestRecord(
            entity.RequestId,
            entity.UserId,
            entity.ApiKeyPrefix,
            entity.ModelId,
            entity.RouterNodeId,
            entity.ProviderName,
            entity.ProviderAccount,
            new TokenUsage(entity.InputTokens, entity.OutputTokens, entity.CacheTokens),
            MoneyAmount.Usd(entity.CostAmount),
            entity.Status,
            entity.CreatedAtUtc,
            entity.CompletedAtUtc);
    }

    public static BillingAnomalyEntity ToEntity(BillingAnomaly anomaly)
    {
        return new BillingAnomalyEntity
        {
            Id = anomaly.Id,
            RequestId = anomaly.RequestId,
            Type = anomaly.Type,
            Message = anomaly.Message,
            CreatedAtUtc = anomaly.CreatedAtUtc
        };
    }

    public static BillingAnomaly ToDomain(BillingAnomalyEntity entity)
    {
        return new BillingAnomaly(entity.Id, entity.RequestId, entity.Type, entity.Message, entity.CreatedAtUtc);
    }
}
