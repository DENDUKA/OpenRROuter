using OpenRussiaRouter.BillingService.Application.Services;
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
            Currency = model.Price.InputPerMillion.Currency.ToString(),
            Enabled = model.Enabled,
            Description = model.Description,
            ContextLength = model.ContextLength,
            Modalities = model.Modalities,
            InputModalities = model.InputModalities,
            OutputModalities = model.OutputModalities,
            DescriptionSource = model.DescriptionSource,
            TagIds = model.TagIds?.ToArray(),
            Provider = model.Provider,
            SupportsReasoning = model.SupportsReasoning,
            AllowedReasoningEfforts = ReasoningEffort.JoinCsv(model.AllowedReasoningEfforts),
            DefaultReasoningEffort = ReasoningEffort.NormalizeNullable(model.DefaultReasoningEffort)
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
                new MoneyAmount(entity.InputPerMillionAmount, Enum.Parse<Currency>(entity.Currency, true)),
                new MoneyAmount(entity.OutputPerMillionAmount, Enum.Parse<Currency>(entity.Currency, true)),
                new MoneyAmount(entity.CachePerMillionAmount, Enum.Parse<Currency>(entity.Currency, true))),
            entity.Enabled,
            Description: entity.Description,
            ContextLength: entity.ContextLength,
            Modalities: entity.Modalities,
            InputModalities: entity.InputModalities,
            OutputModalities: entity.OutputModalities,
            DescriptionSource: entity.DescriptionSource,
            TagIds: entity.TagIds,
            Provider: entity.Provider,
            SupportsReasoning: entity.SupportsReasoning,
            AllowedReasoningEfforts: ReasoningEffort.SplitCsv(entity.AllowedReasoningEfforts),
            DefaultReasoningEffort: ReasoningEffort.NormalizeNullable(entity.DefaultReasoningEffort));
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

    public static TagEntity ToEntity(Tag tag)
    {
        return new TagEntity
        {
            Id = tag.Id,
            DisplayName = tag.DisplayName,
            Color = tag.Color
        };
    }

    public static Tag ToDomain(TagEntity entity)
    {
        return new Tag(entity.Id, entity.DisplayName, entity.Color);
    }

    public static PaymentEntity ToEntity(Payment payment) => new()
    {
        Id = payment.Id,
        UserId = payment.UserId,
        Provider = payment.Provider,
        ProviderPaymentId = payment.ProviderPaymentId,
        OrderId = payment.OrderId,
        ClientToken = payment.ClientToken,
        DisplayAmount = payment.DisplayAmount,
        DisplayCurrency = payment.DisplayCurrency,
        ProviderAmount = payment.ProviderAmount,
        ProviderCurrency = payment.ProviderCurrency,
        CreditedUsdAmount = payment.CreditedUsd.Amount,
        FxRate = payment.FxRate,
        FxSource = payment.FxSource,
        FxAtUtc = payment.FxAtUtc,
        Status = payment.Status,
        CancelReason = payment.CancelReason,
        PayUrl = payment.PayUrl,
        LeaseOwner = payment.LeaseOwner,
        LeaseUntilUtc = payment.LeaseUntilUtc,
        CreatedAtUtc = payment.CreatedAtUtc,
        UpdatedAtUtc = payment.UpdatedAtUtc,
        ExpiresAtUtc = payment.ExpiresAtUtc,
        ConfirmedAtUtc = payment.ConfirmedAtUtc,
        CanceledAtUtc = payment.CanceledAtUtc
    };

    public static Payment ToDomain(PaymentEntity entity) => new(
        entity.Id, entity.UserId, entity.Provider, entity.ProviderPaymentId, entity.OrderId,
        entity.ClientToken, entity.DisplayAmount, entity.DisplayCurrency, entity.ProviderAmount,
        entity.ProviderCurrency, MoneyAmount.Usd(entity.CreditedUsdAmount), entity.FxRate,
        entity.FxSource, entity.FxAtUtc, entity.Status, entity.CancelReason, entity.LeaseOwner,
        entity.LeaseUntilUtc, entity.CreatedAtUtc, entity.UpdatedAtUtc, entity.ExpiresAtUtc,
        entity.ConfirmedAtUtc, entity.CanceledAtUtc, entity.PayUrl);

    public static PaymentEvent ToDomain(PaymentEventEntity entity) => new(
        entity.Id, entity.PaymentId, entity.Provider, entity.ProviderEventId, entity.ProviderPaymentId,
        entity.EventType, entity.OrderId, entity.ConfirmedAmount, entity.ConfirmedCurrency, entity.Reason,
        entity.RawPayload, entity.SignatureOk, entity.Processed, entity.Attempts, entity.ReceivedAtUtc,
        entity.ProcessedAtUtc);
}
