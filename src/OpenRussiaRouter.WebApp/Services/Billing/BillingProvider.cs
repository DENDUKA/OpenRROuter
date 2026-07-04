using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingTokenUsage = OpenRussiaRouter.Contracts.Grpc.Billing.V1.TokenUsage;

namespace OpenRussiaRouter.WebApp.Services.Billing;

public class BillingProvider(BillingGrpc.BillingServiceClient client)
{
    public async Task<BalanceInfo> GetBalanceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetBalanceAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetBalanceRequest { UserId = userId }, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new BillingException(response.ErrorCode);
        }
        return new BalanceInfo(decimal.Parse(response.BalanceUsd, System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<(IReadOnlyList<UsageInfo> Items, bool HasMore, string? NextCursor)> ListUsageAsync(string userId, string? afterCursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.ListUsageAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListUsageRequest { UserId = userId, AfterCursor = afterCursor ?? string.Empty }, cancellationToken: cancellationToken);
        var items = response.Records.Select(r => new UsageInfo(
            r.RequestId,
            r.UserId,
            r.ApiKeyId,
            r.ModelId,
            r.Usage?.InputTokens ?? 0,
            r.Usage?.OutputTokens ?? 0,
            r.Usage?.CacheTokens ?? 0,
            decimal.Parse(r.CostUsd, System.Globalization.CultureInfo.InvariantCulture),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        return (items, response.HasMore, response.NextCursor);
    }

    public async Task<string?> RecordUsageAsync(
        string requestId,
        string userId,
        string apiKeyId,
        string apiKeyPrefix,
        string modelId,
        string routerNodeId,
        long inputTokens,
        long outputTokens,
        long cacheTokens,
        string status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var response = await client.RecordUsageAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.RecordUsageRequest
            {
                RequestId = requestId,
                UserId = userId,
                ApiKeyId = apiKeyId,
                ApiKeyPrefix = apiKeyPrefix,
                ModelId = modelId,
                RouterNodeId = routerNodeId,
                Usage = new BillingTokenUsage
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    CacheTokens = cacheTokens
                },
                Status = status,
                StartedAtUtc = startedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                CompletedAtUtc = completedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken);

        return response.Accepted ? null : response.ErrorCode;
    }

    public virtual async Task<(IReadOnlyList<ActivityByTokenInfo> Items, bool HasMore, string? NextCursor)> ListActivityByTokenAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelIdFilter = null,
        string? apiKeyIdFilter = null,
        string? afterCursor = null,
        int pageSize = 0,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
            return ([], false, null);

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListActivityByTokenRequest
        {
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ModelIdFilter = modelIdFilter ?? string.Empty,
            ApiKeyIdFilter = apiKeyIdFilter ?? string.Empty,
            AfterCursor = afterCursor ?? string.Empty,
            PageSize = pageSize
        };
        request.ApiKeyIds.Add(apiKeyIds);

        var response = await client.ListActivityByTokenAsync(request, cancellationToken: cancellationToken);

        var items = response.Records.Select(r => new ActivityByTokenInfo(
            r.RequestId,
            r.ApiKeyId,
            r.ApiKeyPrefix,
            r.ModelId,
            r.RouterNodeId,
            r.Usage?.InputTokens ?? 0,
            r.Usage?.OutputTokens ?? 0,
            r.Usage?.CacheTokens ?? 0,
            string.IsNullOrEmpty(r.CostUsd) ? 0m : decimal.Parse(r.CostUsd, CultureInfo.InvariantCulture),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(r.CompletedAtUtc, CultureInfo.InvariantCulture))).ToList();

        return (items, response.HasMore, string.IsNullOrEmpty(response.NextCursor) ? null : response.NextCursor);
    }

    public async Task<ApiKeyLimitSettingsInfo> GetApiKeyLimitSettingsAsync(
        string userId,
        string apiKeyId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetApiKeyLimitSettingsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetApiKeyLimitSettingsRequest
            {
                UserId = userId,
                ApiKeyId = apiKeyId
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new BillingException(response.ErrorCode);
        }

        return new ApiKeyLimitSettingsInfo(
            apiKeyId,
            ParseOptionalDecimal(response.DailyLimitUsd),
            ParseOptionalDecimal(response.WeeklyLimitUsd));
    }

    public async Task UpsertApiKeyLimitSettingsAsync(
        string userId,
        string apiKeyId,
        decimal? dailyLimitUsd,
        decimal? weeklyLimitUsd,
        CancellationToken cancellationToken = default)
    {
        var response = await client.UpsertApiKeyLimitSettingsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertApiKeyLimitSettingsRequest
            {
                UserId = userId,
                ApiKeyId = apiKeyId,
                DailyLimitUsd = dailyLimitUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                WeeklyLimitUsd = weeklyLimitUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            },
            cancellationToken: cancellationToken);

        if (!response.Saved)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task<IReadOnlyDictionary<string, ApiKeyLimitSummaryInfo>> ListApiKeyLimitSummariesAsync(
        string userId,
        IReadOnlyCollection<string> apiKeyIds,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
            return new Dictionary<string, ApiKeyLimitSummaryInfo>(StringComparer.Ordinal);

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListApiKeyLimitSummariesRequest
        {
            UserId = userId
        };
        request.ApiKeyIds.AddRange(apiKeyIds);

        var response = await client.ListApiKeyLimitSummariesAsync(request, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            throw new BillingException(response.ErrorCode);
        }

        return response.Summaries
            .Select(summary => new ApiKeyLimitSummaryInfo(
                summary.ApiKeyId,
                ParseOptionalDecimal(summary.DailyLimitUsd),
                ParseOptionalDecimal(summary.WeeklyLimitUsd),
                ParseDecimal(summary.DailySpentUsd),
                ParseDecimal(summary.WeeklySpentUsd),
                ParseDecimal(summary.DailyRemainingUsd),
                ParseDecimal(summary.WeeklyRemainingUsd),
                summary.DailyExceeded,
                summary.WeeklyExceeded))
            .ToDictionary(summary => summary.ApiKeyId, StringComparer.Ordinal);
    }

    public virtual async Task<ActivitySummaryInfo> GetActivitySummaryAsync(
        IReadOnlyCollection<string> apiKeyIds,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
            return new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []);

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetActivitySummaryRequest
        {
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };
        request.ApiKeyIds.AddRange(apiKeyIds);
        var response = await client.GetActivitySummaryAsync(request, cancellationToken: cancellationToken);

        return new ActivitySummaryInfo(
            response.TotalRequests,
            response.TotalInputTokens,
            response.TotalOutputTokens,
            response.TotalCacheTokens,
            string.IsNullOrEmpty(response.TotalCostUsd) ? 0m : decimal.Parse(response.TotalCostUsd, CultureInfo.InvariantCulture),
            response.Models.Select(m => new ActivityModelSummaryInfo(
                m.ModelId, m.Requests, m.InputTokens, m.OutputTokens, m.CacheTokens,
                string.IsNullOrEmpty(m.CostUsd) ? 0m : decimal.Parse(m.CostUsd, CultureInfo.InvariantCulture))).ToList(),
            response.Keys.Select(k => new ActivityKeySummaryInfo(
                k.ApiKeyId, k.ApiKeyPrefix, k.Requests, k.InputTokens, k.OutputTokens, k.CacheTokens,
                string.IsNullOrEmpty(k.CostUsd) ? 0m : decimal.Parse(k.CostUsd, CultureInfo.InvariantCulture))).ToList());
    }

    public async Task<IReadOnlyList<TopUpInfo>> ListTopUpsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.ListTopUpsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListTopUpsRequest { UserId = userId },
            cancellationToken: cancellationToken);
        return response.Records.Select(r => new TopUpInfo(
            decimal.Parse(r.AmountUsd, System.Globalization.CultureInfo.InvariantCulture),
            r.Reason,
            DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }

    public async Task CreateVirtualTopUpAsync(string adminId, string userId, decimal amountUsd, string reason, CancellationToken cancellationToken = default)
    {
        var response = await client.CreateVirtualTopUpAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.CreateVirtualTopUpRequest
            {
                AdminId = adminId,
                UserId = userId,
                AmountUsd = amountUsd.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Reason = reason,
                IdempotencyKey = Guid.NewGuid().ToString()
            }, cancellationToken: cancellationToken);
        if (!response.Created)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListPublicModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListPublicModelsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListPublicModelsRequest(), cancellationToken: cancellationToken);
        return response.Models.Select(m => new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            "",
            string.IsNullOrEmpty(m.InputPricePer1MUsd) ? 0m : decimal.Parse(m.InputPricePer1MUsd, CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(m.OutputPricePer1MUsd) ? 0m : decimal.Parse(m.OutputPricePer1MUsd, CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(m.CachePricePer1MUsd) ? 0m : decimal.Parse(m.CachePricePer1MUsd, CultureInfo.InvariantCulture),
            true,
            Description: string.IsNullOrEmpty(m.Description) ? null : m.Description,
            ContextLength: m.ContextLength == 0 ? null : (int?)m.ContextLength,
            Modalities: string.IsNullOrEmpty(m.Modalities) ? null : m.Modalities,
            InputModalities: string.IsNullOrEmpty(m.InputModalities) ? null : m.InputModalities,
            OutputModalities: string.IsNullOrEmpty(m.OutputModalities) ? null : m.OutputModalities,
            Provider: string.IsNullOrEmpty(m.Provider) ? null : m.Provider,
            TagIds: m.TagIds.Count > 0 ? m.TagIds.ToList() : null,
            ModelType: string.IsNullOrEmpty(m.ModelType) ? "text" : m.ModelType,
            SupportsReasoning: m.SupportsReasoning,
            AllowedReasoningEfforts: m.AllowedReasoningEfforts.Count > 0 ? m.AllowedReasoningEfforts.ToList() : null,
            Priority: m.Priority,
            ProviderId: string.IsNullOrEmpty(m.ProviderId) ? null : m.ProviderId)).ToList();
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListModelsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListModelsRequest(), cancellationToken: cancellationToken);
        return response.Models.Select(m => new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            m.PriceVersion,
            decimal.Parse(m.InputPricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.OutputPricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.CachePricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            m.Enabled,
            Description: string.IsNullOrEmpty(m.Description) ? null : m.Description,
            ContextLength: m.ContextLength == 0 ? null : (int?)m.ContextLength,
            Modalities: string.IsNullOrEmpty(m.Modalities) ? null : m.Modalities,
            InputModalities: string.IsNullOrEmpty(m.InputModalities) ? null : m.InputModalities,
            OutputModalities: string.IsNullOrEmpty(m.OutputModalities) ? null : m.OutputModalities,
            DescriptionSource: string.IsNullOrEmpty(m.DescriptionSource) ? null : m.DescriptionSource,
            Provider: string.IsNullOrEmpty(m.Provider) ? null : m.Provider,
            TagIds: m.TagIds.Count > 0 ? m.TagIds.ToList() : null,
            ModelType: string.IsNullOrEmpty(m.ModelType) ? "text" : m.ModelType,
            SupportsReasoning: m.SupportsReasoning,
            AllowedReasoningEfforts: m.AllowedReasoningEfforts.Count > 0 ? m.AllowedReasoningEfforts.ToList() : null,
            DefaultReasoningEffort: string.IsNullOrEmpty(m.DefaultReasoningEffort) ? null : m.DefaultReasoningEffort,
            Priority: m.Priority,
            ProviderId: string.IsNullOrEmpty(m.ProviderId) ? null : m.ProviderId)).ToList();
    }

    public async Task<ModelInfo?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetModelAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetModelRequest { ModelId = modelId }, cancellationToken: cancellationToken);
        if (!response.Found || response.Model is null)
            return null;

        var m = response.Model;
        return new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            m.PriceVersion,
            decimal.Parse(m.InputPricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.OutputPricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(m.CachePricePer1MUsd, System.Globalization.CultureInfo.InvariantCulture),
            m.Enabled,
            Description: string.IsNullOrEmpty(m.Description) ? null : m.Description,
            ContextLength: m.ContextLength == 0 ? null : (int?)m.ContextLength,
            Modalities: string.IsNullOrEmpty(m.Modalities) ? null : m.Modalities,
            InputModalities: string.IsNullOrEmpty(m.InputModalities) ? null : m.InputModalities,
            OutputModalities: string.IsNullOrEmpty(m.OutputModalities) ? null : m.OutputModalities,
            DescriptionSource: string.IsNullOrEmpty(m.DescriptionSource) ? null : m.DescriptionSource,
            Provider: string.IsNullOrEmpty(m.Provider) ? null : m.Provider,
            TagIds: m.TagIds.Count > 0 ? m.TagIds.ToList() : null,
            ModelType: string.IsNullOrEmpty(m.ModelType) ? "text" : m.ModelType,
            SupportsReasoning: m.SupportsReasoning,
            AllowedReasoningEfforts: m.AllowedReasoningEfforts.Count > 0 ? m.AllowedReasoningEfforts.ToList() : null,
            DefaultReasoningEffort: string.IsNullOrEmpty(m.DefaultReasoningEffort) ? null : m.DefaultReasoningEffort,
            ProviderId: string.IsNullOrEmpty(m.ProviderId) ? null : m.ProviderId);
    }

    public async Task UpsertModelAsync(ModelInfo model, CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertModelPriceRequest
        {
            ModelId = model.Id,
            DisplayName = model.DisplayName,
            UpstreamModelId = model.UpstreamModelId,
            PriceVersion = model.PriceVersion,
            InputPricePer1MUsd = model.InputPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OutputPricePer1MUsd = model.OutputPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CachePricePer1MUsd = model.CachePrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Enabled = model.Enabled,
            Description = model.Description ?? string.Empty,
            ContextLength = model.ContextLength ?? 0,
            Modalities = model.Modalities ?? string.Empty,
            InputModalities = model.InputModalities ?? string.Empty,
            OutputModalities = model.OutputModalities ?? string.Empty,
            DescriptionSource = model.DescriptionSource ?? string.Empty,
            Provider = model.Provider ?? string.Empty,
            ProviderId = model.ProviderId ?? string.Empty,
            SupportsReasoning = model.SupportsReasoning,
            DefaultReasoningEffort = model.DefaultReasoningEffort ?? string.Empty,
            ModelType = model.ModelType ?? "text",
            Priority = model.Priority
        };
        if (model.TagIds != null)
        {
            request.TagIds.AddRange(model.TagIds);
        }
        if (model.AllowedReasoningEfforts != null)
        {
            request.AllowedReasoningEfforts.AddRange(model.AllowedReasoningEfforts);
        }

        var response = await client.UpsertModelPriceAsync(request, cancellationToken: cancellationToken);
        if (!response.Upserted)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task<IReadOnlyList<AnomalyInfo>> ListAnomaliesAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListBillingAnomaliesAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListBillingAnomaliesRequest(), cancellationToken: cancellationToken);
        return response.Anomalies.Select(a => new AnomalyInfo(
            a.Id,
            a.RequestId,
            a.Type,
            a.Message,
            DateTimeOffset.Parse(a.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }

    public async Task<(IReadOnlyList<AdminRequestInfo> Items, bool HasMore, string? NextCursor)> ListAdminRequestsAsync(
        string? afterCursor = null,
        int pageSize = 50,
        string? modelFilter = null,
        string? providerFilter = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListAdminRequestsRequest
        {
            AfterCursor = afterCursor ?? string.Empty,
            PageSize = pageSize,
            ModelFilter = modelFilter ?? string.Empty,
            ProviderFilter = providerFilter ?? string.Empty,
            FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };
        var response = await client.ListAdminRequestsAsync(request, cancellationToken: cancellationToken);

        var items = response.Records.Select(r => new AdminRequestInfo(
            r.RequestId,
            r.UserId,
            r.ApiKeyPrefix,
            r.ModelId,
            r.RouterNodeId,
            string.IsNullOrEmpty(r.ProviderName) ? null : r.ProviderName,
            string.IsNullOrEmpty(r.ProviderAccount) ? null : r.ProviderAccount,
            r.InputTokens,
            r.OutputTokens,
            r.CacheTokens,
            string.IsNullOrEmpty(r.CostUsd) ? 0m : decimal.Parse(r.CostUsd, CultureInfo.InvariantCulture),
            r.Status,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(r.CompletedAtUtc, CultureInfo.InvariantCulture))).ToList();

        return (items, response.Records.Count >= pageSize && !string.IsNullOrEmpty(response.NextCursor),
            string.IsNullOrEmpty(response.NextCursor) ? null : response.NextCursor);
    }

    public async Task<IReadOnlyList<TagInfo>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListTagsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListTagsRequest(), cancellationToken: cancellationToken);
        return response.Tags.Select(t => new TagInfo(
            t.Id,
            t.DisplayName,
            string.IsNullOrEmpty(t.Color) ? null : t.Color)).ToList();
    }

    public async Task UpsertTagAsync(TagInfo tag, CancellationToken cancellationToken = default)
    {
        var response = await client.UpsertTagAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertTagRequest
        {
            Id = tag.Id,
            DisplayName = tag.DisplayName,
            Color = tag.Color ?? string.Empty
        }, cancellationToken: cancellationToken);
        if (!response.Upserted)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task DeleteTagAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteTagAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.DeleteTagRequest { Id = id }, cancellationToken: cancellationToken);
        if (!response.Deleted)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task<string?> GetPlatformSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await client.GetPlatformSettingAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetPlatformSettingRequest { Key = key },
            cancellationToken: cancellationToken);
        return response.Found ? response.Value : null;
    }

    public async Task SetPlatformSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var response = await client.SetPlatformSettingAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.SetPlatformSettingRequest { Key = key, Value = value ?? string.Empty },
            cancellationToken: cancellationToken);
        if (!response.Ok)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

   // ----- TS-004: payment gRPC wrappers -----
   //
   // WebApp never computes credited_usd — Billing does that from its trusted FX quote.
   // WebApp only sends provider_amount/provider_currency (what the user pays) and display_*
   // (for the user-facing history). The s2s token is attached by the registered interceptor.

   /// <summary>
   /// Get the trusted FX quote for a given provider currency. Used by the checkout UI to show
   /// the user a preview of the credited amount. The authoritative source for the actual credit
   /// is BillingService itself at <c>CreatePayment</c> time.
   /// </summary>
   public virtual async Task<(decimal Rate, string FxSource, DateTimeOffset FxAt, string? QuoteId)> GetFxQuoteAsync(
       string providerCurrency,
       CancellationToken cancellationToken = default)
   {
       var response = await client.GetFxQuoteAsync(
           new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetFxQuoteRequest
           {
               ProviderCurrency = providerCurrency
           },
           cancellationToken: cancellationToken);

       if (!string.IsNullOrWhiteSpace(response.ErrorCode))
       {
           throw new BillingException(response.ErrorCode);
       }

       return (
           Rate: decimal.Parse(response.Rate, CultureInfo.InvariantCulture),
           FxSource: response.FxSource,
           FxAt: DateTimeOffset.Parse(response.FxAt, CultureInfo.InvariantCulture),
           QuoteId: string.IsNullOrEmpty(response.QuoteId) ? null : response.QuoteId);
   }

   /// <summary>
   /// Create a payment row in Billing. Returns the internal payment id (= order id sent to the
   /// provider) and the credited USD amount Billing computed from its trusted FX quote.
   /// Idempotent by <paramref name="clientToken"/> per user.
   /// </summary>
   public virtual async Task<CreatedPaymentInfo> CreatePaymentAsync(
       string userId,
       string provider,
       decimal providerAmount,
       string providerCurrency,
       decimal displayAmount,
       string displayCurrency,
       string clientToken,
       CancellationToken cancellationToken = default)
   {
       var response = await client.CreatePaymentAsync(
           new OpenRussiaRouter.Contracts.Grpc.Billing.V1.CreatePaymentRequest
           {
               UserId = userId,
               Provider = provider,
               ProviderAmount = providerAmount.ToString(CultureInfo.InvariantCulture),
               ProviderCurrency = providerCurrency,
               DisplayAmount = displayAmount.ToString(CultureInfo.InvariantCulture),
               DisplayCurrency = displayCurrency,
               ClientToken = clientToken
           },
           cancellationToken: cancellationToken);

       if (!string.IsNullOrWhiteSpace(response.ErrorCode))
       {
           throw new BillingException(response.ErrorCode);
       }

       return new CreatedPaymentInfo(
           PaymentId: response.PaymentId,
           OrderId: response.OrderId,
           CreditedUsdAmount: decimal.Parse(response.CreditedUsdAmount, CultureInfo.InvariantCulture),
           FxRate: decimal.Parse(response.FxRate, CultureInfo.InvariantCulture));
   }

   /// <summary>
   /// Attach the provider-side payment id and pay URL to an existing payment. Atomic on the
   /// Billing side — fails if the payment is no longer in <c>Created</c> (e.g. expired meanwhile).
   /// </summary>
   public virtual async Task AttachProviderRefAsync(
       string paymentId,
       string providerPaymentId,
       string payUrl,
       DateTimeOffset? expiresAtUtc,
       CancellationToken cancellationToken = default)
   {
       var response = await client.AttachProviderRefAsync(
           new OpenRussiaRouter.Contracts.Grpc.Billing.V1.AttachProviderRefRequest
           {
               PaymentId = paymentId,
               ProviderPaymentId = providerPaymentId,
               PayUrl = payUrl,
               ExpiresAtUtc = expiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
           },
           cancellationToken: cancellationToken);

       if (!response.Ok)
       {
           throw new BillingException(response.ErrorCode);
       }
   }

   /// <summary>
   /// Confirm a payment as paid. Idempotent: re-calls with the same provider event resolve to
   /// a no-op and return <c>Confirmed=true</c> with the original ledger entry id.
   /// </summary>
   public virtual async Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
       string provider,
       string? providerPaymentId,
       string? orderId,
       decimal confirmedAmount,
       string confirmedCurrency,
       string? providerEventId,
       string rawPayload,
       bool signatureOk,
       CancellationToken cancellationToken = default)
   {
       var response = await client.ConfirmPaymentByProviderAsync(
           new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ConfirmPaymentByProviderRequest
           {
               Provider = provider,
               ProviderPaymentId = providerPaymentId ?? string.Empty,
               OrderId = orderId ?? string.Empty,
               ConfirmedAmount = confirmedAmount.ToString(CultureInfo.InvariantCulture),
               ConfirmedCurrency = confirmedCurrency,
               ProviderEventId = providerEventId ?? string.Empty,
               RawPayload = rawPayload,
               SignatureOk = signatureOk
           },
           cancellationToken: cancellationToken);

       return new ConfirmPaymentResult(
           Confirmed: response.Confirmed,
           LedgerEntryId: string.IsNullOrEmpty(response.LedgerEntryId) ? null : response.LedgerEntryId,
           BalanceUsd: string.IsNullOrEmpty(response.BalanceUsd) ? (decimal?)null : decimal.Parse(response.BalanceUsd, CultureInfo.InvariantCulture),
           ErrorCode: string.IsNullOrEmpty(response.ErrorCode) ? null : response.ErrorCode);
   }

   /// <summary>
   /// Apply a non-paid provider event (canceled / expired / refunded / chargeback). Billing
   /// handles the state transition and, for refund/chargeback, the ledger reversal atomically.
   /// </summary>
   public virtual async Task<ApplyPaymentEventResult> ApplyPaymentEventAsync(
       string provider,
       string? providerPaymentId,
       string? orderId,
       string eventType,
       string? reason,
       decimal? confirmedAmount,
       string? confirmedCurrency,
       string? providerEventId,
       string rawPayload,
       bool signatureOk,
       CancellationToken cancellationToken = default)
   {
       var response = await client.ApplyPaymentEventAsync(
           new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ApplyPaymentEventRequest
           {
               Provider = provider,
               ProviderPaymentId = providerPaymentId ?? string.Empty,
               OrderId = orderId ?? string.Empty,
               EventType = eventType,
               Reason = reason ?? string.Empty,
               ConfirmedAmount = confirmedAmount.HasValue ? confirmedAmount.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
               ConfirmedCurrency = confirmedCurrency ?? string.Empty,
               ProviderEventId = providerEventId ?? string.Empty,
               RawPayload = rawPayload,
               SignatureOk = signatureOk
           },
           cancellationToken: cancellationToken);

       return new ApplyPaymentEventResult(
           Applied: response.Applied,
           LedgerEntryId: string.IsNullOrEmpty(response.LedgerEntryId) ? null : response.LedgerEntryId,
           ErrorCode: string.IsNullOrEmpty(response.ErrorCode) ? null : response.ErrorCode);
   }

   /// <summary>
   /// Get a single payment by its internal id. Returns <c>null</c> if the payment does not exist.
   /// </summary>
   public virtual async Task<PaymentInfo?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
   {
       try
       {
           var record = await client.GetPaymentAsync(
               new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetPaymentRequest { PaymentId = paymentId },
               cancellationToken: cancellationToken);
           return PaymentRecordMapper.ToInfo(record);
       }
       catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
       {
           return null;
       }
   }

   /// <summary>
   /// Acquire a batch of stale <c>Created</c> payments for reconciliation. Billing leases each
   /// row to <paramref name="workerId"/> for <paramref name="leaseSeconds"/> using SKIP LOCKED,
   /// so multiple WebApp replicas can sweep concurrently without collision.
   /// </summary>
   public virtual async Task<IReadOnlyList<PaymentInfo>> AcquireStalePaymentsAsync(
       string workerId,
       int leaseSeconds,
       DateTimeOffset olderThanUtc,
       int limit,
       CancellationToken cancellationToken = default)
   {
       var response = await client.AcquireStalePaymentsAsync(
           new OpenRussiaRouter.Contracts.Grpc.Billing.V1.AcquireStalePaymentsRequest
           {
               WorkerId = workerId,
               LeaseSeconds = leaseSeconds,
               OlderThanUtc = olderThanUtc.ToString("O", CultureInfo.InvariantCulture),
               Limit = limit
           },
           cancellationToken: cancellationToken);

       return response.Records.Select(PaymentRecordMapper.ToInfo).ToList();
   }

   private static decimal? ParseOptionalDecimal(string value) =>
       string.IsNullOrWhiteSpace(value) ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<ProviderInfo>> ListModelProvidersAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListModelProvidersAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListProvidersRequest(), cancellationToken: cancellationToken);
        return response.Providers.Select(p => new ProviderInfo(
            p.Id,
            p.DisplayName,
            string.IsNullOrEmpty(p.DescriptionRu) ? null : p.DescriptionRu,
            string.IsNullOrEmpty(p.DescriptionEn) ? null : p.DescriptionEn,
            p.LogoBytes.Length > 0 ? p.LogoBytes.ToByteArray() : null,
            string.IsNullOrEmpty(p.LogoContentType) ? null : p.LogoContentType,
            string.IsNullOrEmpty(p.CreatedAtUtc) ? default : DateTimeOffset.Parse(p.CreatedAtUtc, CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(p.UpdatedAtUtc) ? default : DateTimeOffset.Parse(p.UpdatedAtUtc, CultureInfo.InvariantCulture))).ToList();
    }

    public async Task<ProviderInfo?> GetModelProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetModelProviderAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetProviderRequest { ProviderId = providerId },
            cancellationToken: cancellationToken);
        if (!response.Found || response.Provider is null)
            return null;
        var p = response.Provider;
        return new ProviderInfo(
            p.Id,
            p.DisplayName,
            string.IsNullOrEmpty(p.DescriptionRu) ? null : p.DescriptionRu,
            string.IsNullOrEmpty(p.DescriptionEn) ? null : p.DescriptionEn,
            p.LogoBytes.Length > 0 ? p.LogoBytes.ToByteArray() : null,
            string.IsNullOrEmpty(p.LogoContentType) ? null : p.LogoContentType,
            string.IsNullOrEmpty(p.CreatedAtUtc) ? default : DateTimeOffset.Parse(p.CreatedAtUtc, CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(p.UpdatedAtUtc) ? default : DateTimeOffset.Parse(p.UpdatedAtUtc, CultureInfo.InvariantCulture));
    }

    public async Task UpsertModelProviderAsync(ProviderInfo provider, CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertProviderRequest
        {
            Id = provider.Id,
            DisplayName = provider.DisplayName,
            DescriptionRu = provider.DescriptionRu ?? string.Empty,
            DescriptionEn = provider.DescriptionEn ?? string.Empty,
            LogoContentType = provider.LogoContentType ?? string.Empty,
            ClearLogo = provider.LogoBytes is null && provider.LogoContentType is null
        };
        if (provider.LogoBytes is { Length: > 0 })
        {
            request.LogoBytes = Google.Protobuf.ByteString.CopyFrom(provider.LogoBytes);
        }

        var response = await client.UpsertModelProviderAsync(request, cancellationToken: cancellationToken);
        if (!response.Upserted)
        {
            throw new BillingException(response.ErrorCode);
        }
    }

    public async Task DeleteModelProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteModelProviderAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.DeleteProviderRequest { ProviderId = providerId },
            cancellationToken: cancellationToken);
        if (!response.Deleted)
        {
            throw new BillingException(response.ErrorCode);
        }
    }
}
