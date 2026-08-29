using System.Collections.Concurrent;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed partial class BillingRepository(IBillingDbConnectionFactory connectionFactory) : IBillingRepository
{
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset ExpiresAt)> _topUpsCountCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan TopUpsCountCacheTtl = TimeSpan.FromSeconds(60);

    private const string LedgerEntrySelectSql = """
        select
            id as Id,
            user_id as UserId,
            type as Type,
            amount as Amount,
            currency as Currency,
            idempotency_key as IdempotencyKey,
            reason as Reason,
            created_at_utc as CreatedAtUtc
        from billing.ledger_entries

        """;

    private const string ModelPriceSelectSql = """
        select
            model_id as ModelId,
            billing.model_prices.display_name as DisplayName,
            router_model_id as RouterModelId,
            price_version as PriceVersion,
            input_per_million_amount as InputPerMillionAmount,
            output_per_million_amount as OutputPerMillionAmount,
            cache_per_million_amount as CachePerMillionAmount,
            currency as Currency,
            enabled as Enabled,
            billing.model_prices.description_ru as DescriptionRu,
            billing.model_prices.description_en as DescriptionEn,
            billing.model_prices.context_length as ContextLength,
            billing.model_prices.modalities as Modalities,
            billing.model_prices.input_modalities as InputModalities,
            billing.model_prices.output_modalities as OutputModalities,
            billing.model_prices.description_source as DescriptionSource,
            billing.model_prices.tag_ids as TagIds,
            coalesce(model_provider.display_name, billing.model_prices.provider) as Provider,
            billing.model_prices.provider_id as ProviderId,
            billing.model_prices.supports_reasoning as SupportsReasoning,
            billing.model_prices.allowed_reasoning_efforts as AllowedReasoningEfforts,
            billing.model_prices.default_reasoning_effort as DefaultReasoningEffort,
            billing.model_prices.reasoning_efforts_overridden as ReasoningEffortsOverridden,
            billing.model_prices.model_type as ModelType,
            billing.model_prices.priority as Priority
        from billing.model_prices
        left join billing.model_providers model_provider
            on model_provider.id = billing.model_prices.provider_id

        """;

    private const string UsageSelectSql = """
        select
            request_id as RequestId,
            user_id as UserId,
            api_key_id as ApiKeyId,
            model_id as ModelId,
            price_version as PriceVersion,
            router_node_id as RouterNodeId,
            input_tokens as InputTokens,
            output_tokens as OutputTokens,
            cache_tokens as CacheTokens,
            cost_amount as CostAmount,
            cost_currency as CostCurrency,
            status as Status,
            created_at_utc as CreatedAtUtc,
            provider_name as ProviderName,
            provider_account as ProviderAccount
        from billing.usage_records

        """;

    private const string UsageByRequestSql = UsageSelectSql + """
        where request_id = @RequestId;
        """;

    private const string ActivityByTokenSelectSql = """
        select
            a.request_id as RequestId,
            a.api_key_id as ApiKeyId,
            a.api_key_prefix as ApiKeyPrefix,
            a.model_id as ModelId,
            a.router_node_id as RouterNodeId,
            a.input_tokens as InputTokens,
            a.output_tokens as OutputTokens,
            a.cache_tokens as CacheTokens,
            coalesce(u.cost_amount, 0) as CostAmount,
            a.status as Status,
            a.created_at_utc as CreatedAtUtc,
            a.completed_at_utc as CompletedAtUtc,
            a.provider_name as ProviderName,
            a.provider_account as ProviderAccount
        from billing.activity_by_token a
        left join billing.usage_records u on u.request_id = a.request_id

        """;

    private const string PaymentSelectColumns = """
        select id as Id, user_id as UserId, provider as Provider, provider_payment_id as ProviderPaymentId,
            order_id as OrderId, client_token as ClientToken, display_amount as DisplayAmount,
            display_currency as DisplayCurrency, provider_amount as ProviderAmount, provider_currency as ProviderCurrency,
            credited_usd_amount as CreditedUsdAmount, fx_rate as FxRate, fx_source as FxSource, fx_at_utc as FxAtUtc,
            status as Status, cancel_reason as CancelReason, pay_url as PayUrl, lease_owner as LeaseOwner, lease_until_utc as LeaseUntilUtc,
            created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc, expires_at_utc as ExpiresAtUtc,
            confirmed_at_utc as ConfirmedAtUtc, canceled_at_utc as CanceledAtUtc,
            bonus_percent as BonusPercent, bonus_usd as BonusUsd
        """;

    private const string PaymentSelectSql = PaymentSelectColumns + """

        from billing.payments

        """;

    private const string PaymentSelectFromUpdatedSql = PaymentSelectColumns + """

        from updated

        """;

    private const string PaymentEventSelectSql = """
        select id as Id, payment_id as PaymentId, provider as Provider, provider_event_id as ProviderEventId,
            provider_payment_id as ProviderPaymentId, event_type as EventType, order_id as OrderId,
            confirmed_amount as ConfirmedAmount, confirmed_currency as ConfirmedCurrency, reason as Reason,
            raw_payload as RawPayload, signature_ok as SignatureOk, processed as Processed, attempts as Attempts,
            received_at_utc as ReceivedAtUtc, processed_at_utc as ProcessedAtUtc
        from billing.payment_events

        """;

    private const string ModelProviderSelectSql =
        """
        select id as Id, display_name as DisplayName, description_ru as DescriptionRu,
               description_en as DescriptionEn, logo_bytes as LogoBytes,
               logo_content_type as LogoContentType,
               created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc
        from billing.model_providers
        """;

    private const string ModelProviderSelectSqlWithoutLogo =
        """
        select id as Id, display_name as DisplayName, description_ru as DescriptionRu,
               description_en as DescriptionEn,
               created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc
        from billing.model_providers
        """;
}
