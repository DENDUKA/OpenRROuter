using Dapper;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

public sealed partial class BillingRepository
{
    public async Task UpsertModelAsync(ModelPrice model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.model_prices (
                model_id, display_name, router_model_id, price_version,
                input_per_million_amount, output_per_million_amount, cache_per_million_amount,
                currency, enabled,
                description_ru, description_en, context_length, modalities, input_modalities, output_modalities, description_source,
                tag_ids, provider, provider_id,
                supports_reasoning, allowed_reasoning_efforts, default_reasoning_effort, reasoning_efforts_overridden, model_type,
                priority, is_flagship,
                openrouter_input_per_million_amount, openrouter_output_per_million_amount, openrouter_cache_per_million_amount
            )
            values (
                @ModelId, @DisplayName, @RouterModelId, @PriceVersion,
                @InputPerMillionAmount, @OutputPerMillionAmount, @CachePerMillionAmount,
                @Currency, @Enabled,
                @DescriptionRu, @DescriptionEn, @ContextLength, @Modalities, @InputModalities, @OutputModalities, @DescriptionSource,
                @TagIds, @Provider, @ProviderId,
                @SupportsReasoning, @AllowedReasoningEfforts, @DefaultReasoningEffort, @DefaultReasoningEffortsOverridden, @ModelType,
                @Priority, @IsFlagship,
                @OpenRouterInputPrice, @OpenRouterOutputPrice, @OpenRouterCachePrice
            )
            on conflict (model_id) do update set
                display_name = excluded.display_name,
                router_model_id = excluded.router_model_id,
                price_version = excluded.price_version,
                input_per_million_amount = excluded.input_per_million_amount,
                output_per_million_amount = excluded.output_per_million_amount,
                cache_per_million_amount = excluded.cache_per_million_amount,
                currency = excluded.currency,
                enabled = excluded.enabled,
                tag_ids = excluded.tag_ids,
                provider = excluded.provider,
                provider_id = excluded.provider_id,
                supports_reasoning = excluded.supports_reasoning,
                allowed_reasoning_efforts = excluded.allowed_reasoning_efforts,
                default_reasoning_effort = excluded.default_reasoning_effort,
                reasoning_efforts_overridden = excluded.reasoning_efforts_overridden,
                model_type = excluded.model_type,
                priority = excluded.priority,
                is_flagship = excluded.is_flagship,
                openrouter_input_per_million_amount = excluded.openrouter_input_per_million_amount,
                openrouter_output_per_million_amount = excluded.openrouter_output_per_million_amount,
                openrouter_cache_per_million_amount = excluded.openrouter_cache_per_million_amount,
                description_ru = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.description_ru
                    ELSE COALESCE(excluded.description_ru, billing.model_prices.description_ru)
                END,
                description_en = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.description_en
                    ELSE COALESCE(excluded.description_en, billing.model_prices.description_en)
                END,
                context_length = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.context_length
                    ELSE COALESCE(excluded.context_length, billing.model_prices.context_length)
                END,
                modalities = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.modalities
                    ELSE COALESCE(excluded.modalities, billing.model_prices.modalities)
                END,
                input_modalities = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.input_modalities
                    ELSE COALESCE(excluded.input_modalities, billing.model_prices.input_modalities)
                END,
                output_modalities = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.output_modalities
                    ELSE COALESCE(excluded.output_modalities, billing.model_prices.output_modalities)
                END,
                description_source = CASE
                    WHEN excluded.description_source IS NOT NULL
                    THEN excluded.description_source
                    ELSE billing.model_prices.description_source
                END
            """,
            new
            {
                model.ModelId,
                model.DisplayName,
                model.RouterModelId,
                model.PriceVersion,
                InputPerMillionAmount = model.Price.InputPerMillion.Amount,
                OutputPerMillionAmount = model.Price.OutputPerMillion.Amount,
                CachePerMillionAmount = model.Price.CachePerMillion.Amount,
                Currency = model.Price.InputPerMillion.Currency.ToString(),
                model.Enabled,
                model.DescriptionRu,
                model.DescriptionEn,
                model.ContextLength,
                model.Modalities,
                model.InputModalities,
                model.OutputModalities,
                model.DescriptionSource,
                model.Provider,
                model.ProviderId,
                TagIds = model.TagIds?.ToArray() ?? [],
                model.SupportsReasoning,
                AllowedReasoningEfforts = ReasoningEffort.JoinCsv(model.AllowedReasoningEfforts),
                DefaultReasoningEffort = model.DefaultReasoningEffort,
                DefaultReasoningEffortsOverridden = model.ReasoningEffortsOverridden,
                ModelType = model.ModelType,
                model.Priority,
                model.IsFlagship,
                model.OpenRouterInputPrice,
                model.OpenRouterOutputPrice,
                model.OpenRouterCachePrice
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ModelPrice?> GetEnabledModelAsync(string modelId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ModelPriceEntity>(new CommandDefinition(
            ModelPriceSelectSql + "where model_id = @ModelId and enabled = true;",
            new { ModelId = modelId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<ModelPrice?> GetModelAsync(string modelId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ModelPriceEntity>(new CommandDefinition(
            ModelPriceSelectSql + "where model_id = @ModelId\n" +
            "union all\n" +
            ModelPriceSelectSql + "where router_model_id = @ModelId and not exists (\n" +
            "    select 1 from billing.model_prices where model_id = @ModelId\n" +
            ")\n" +
            "limit 1;",
            new { ModelId = modelId },
            cancellationToken: ct)).ConfigureAwait(false) is { } entity
                ? BillingPersistenceMapper.ToDomain(entity)
                : null;
    }

    public async Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListEnabledModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterModelId != null)
        {
            afterCondition = "and model_id > @AfterModelId\n";
            parameters.Add("AfterModelId", afterModelId);
        }

        var items = (await connection.QueryAsync<ModelPriceEntity>(new CommandDefinition(
                ModelPriceSelectSql + "where enabled = true\n" + afterCondition + "order by model_id\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<(IReadOnlyCollection<ModelPrice> Items, bool HasMore)> ListAllModelsAsync(int limit, string? afterModelId = null, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit + 1);
        var afterCondition = "";
        if (afterModelId != null)
        {
            afterCondition = "where model_id > @AfterModelId\n";
            parameters.Add("AfterModelId", afterModelId);
        }

        var items = (await connection.QueryAsync<ModelPriceEntity>(new CommandDefinition(
                ModelPriceSelectSql + afterCondition + "order by model_id\nlimit @Limit;",
                parameters,
                cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();

        var hasMore = items.Length > limit;
        if (hasMore) items = items.Take(limit).ToArray();
        return (items, hasMore);
    }

    public async Task<IReadOnlyCollection<Tag>> ListTagsAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<TagEntity>(new CommandDefinition(
            """
            select
                id as Id,
                display_name as DisplayName,
                color as Color
            from billing.tags
            order by display_name;
            """,
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();
    }

    public async Task UpsertTagAsync(Tag tag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.tags (id, display_name, color)
            values (@Id, @DisplayName, @Color)
            on conflict (id) do update set
                display_name = excluded.display_name,
                color = excluded.color;
            """,
            new { tag.Id, tag.DisplayName, tag.Color },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteTagAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var npgsqlConn = (Npgsql.NpgsqlConnection)connection;
        await npgsqlConn.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await npgsqlConn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update billing.model_prices
            set tag_ids = array_remove(tag_ids, @Id)
            where tag_ids @> array[@Id]::text[];
            """,
            new { Id = id },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "delete from billing.tags where id = @Id;",
            new { Id = id },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<ModelProvider>> ListModelProvidersAsync(bool includeLogo = true, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var sql = includeLogo ? ModelProviderSelectSql : ModelProviderSelectSqlWithoutLogo;
        var items = (await connection.QueryAsync<ModelProviderEntity>(new CommandDefinition(
            sql + " order by display_name;",
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(BillingPersistenceMapper.ToDomain)
            .ToArray();
        return items;
    }

    public async Task<ModelProvider?> GetModelProviderAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ModelProviderEntity>(new CommandDefinition(
            ModelProviderSelectSql + " where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : BillingPersistenceMapper.ToDomain(entity);
    }

    public async Task UpsertModelProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into billing.model_providers (id, display_name, description_ru, description_en, logo_bytes, logo_content_type, created_at_utc, updated_at_utc)
            values (@Id, @DisplayName, @DescriptionRu, @DescriptionEn, @LogoBytes, @LogoContentType, @CreatedAtUtc, @UpdatedAtUtc)
            on conflict (id) do update set
                display_name = excluded.display_name,
                description_ru = excluded.description_ru,
                description_en = excluded.description_en,
                logo_bytes = excluded.logo_bytes,
                logo_content_type = excluded.logo_content_type,
                updated_at_utc = excluded.updated_at_utc;
            """,
            BillingPersistenceMapper.ToEntity(provider),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ModelProvider?> GetModelProviderLogoAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ModelProviderEntity>(new CommandDefinition(
            ModelProviderSelectSql + " where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : BillingPersistenceMapper.ToDomain(entity);
    }

    public async Task DeleteModelProviderAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update billing.model_prices set provider_id = null where provider_id = @Id;
            delete from billing.model_providers where id = @Id;
            """,
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}
