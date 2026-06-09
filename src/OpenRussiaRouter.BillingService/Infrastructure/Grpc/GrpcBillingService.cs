using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Money;
using OpenRussiaRouter.BuildingBlocks.Usage;
using OpenRussiaRouter.Contracts.Billing;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.BillingService.Infrastructure.Grpc;

public sealed class GrpcBillingService(
    BillingApplicationService billing,
    ILogger<GrpcBillingService> logger) : BillingGrpc.BillingServiceBase
{
    public override async Task<CheckBalanceAndModelResponse> CheckBalanceAndModel(
        CheckBalanceAndModelRequest request,
        ServerCallContext context)
    {
        var result = await billing.CheckBalanceAndModelAsync(request.UserId, request.ModelId, request.AdminBypassDisabled, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        var response = new CheckBalanceAndModelResponse
        {
            Allowed = result.Allowed,
            ModelId = result.ModelId,
            RouterModelId = result.RouterModelId,
            PriceVersion = result.PriceVersion ?? string.Empty,
            BalanceUsd = result.CurrentBalance.Amount.ToString(CultureInfo.InvariantCulture),
            ErrorCode = result.ErrorCode ?? string.Empty,
            SupportsReasoning = result.SupportsReasoning
        };
        if (result.AllowedReasoningEfforts is not null)
        {
            response.AllowedReasoningEfforts.AddRange(result.AllowedReasoningEfforts);
        }

        return response;
    }

    public override async Task<ListPublicModelsResponse> ListPublicModels(
        ListPublicModelsRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var publicModels = await billing.ListPublicModelsAsync(ct).ConfigureAwait(false);
        var pricesByRouterId = (await billing.ListAllModelsAsync(ct).ConfigureAwait(false))
            .ToDictionary(model => model.RouterModelId, StringComparer.Ordinal);

        var response = new ListPublicModelsResponse();
        response.Models.AddRange(
            publicModels.Select(model =>
            {
                var price = pricesByRouterId.Values.FirstOrDefault(p => p.ModelId == model.Id);

                var modelResponse = new Contracts.Grpc.Billing.V1.PublicModel
                {
                    Id = model.Id,
                    DisplayName = model.DisplayName,
                    UpstreamModelId = price?.RouterModelId ?? string.Empty,
                    InputPricePer1MUsd = price?.Price.InputPerMillion.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    OutputPricePer1MUsd = price?.Price.OutputPerMillion.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    CachePricePer1MUsd = price?.Price.CachePerMillion.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    ContextLength = price?.ContextLength ?? 0,
                    Modalities = price?.Modalities ?? string.Empty,
                    Description = price?.Description ?? string.Empty,
                    Provider = price?.Provider ?? string.Empty,
                    SupportsReasoning = model.SupportsReasoning,
                };
                if (model.AllowedReasoningEfforts is not null)
                {
                    modelResponse.AllowedReasoningEfforts.AddRange(model.AllowedReasoningEfforts);
                }
                if (price?.TagIds != null)
                {
                    modelResponse.TagIds.AddRange(price.TagIds);
                }
                return modelResponse;
            }));
        return response;
    }

    public override async Task<RecordUsageResponse> RecordUsage(
        RecordUsageRequest request,
        ServerCallContext context)
    {
        var usageEvent = new UsageEvent(
            request.RequestId,
            request.UserId,
            request.ApiKeyId,
            request.ApiKeyPrefix,
            request.ModelId,
            request.RouterNodeId,
            new BuildingBlocks.Usage.TokenUsage(
                request.Usage?.InputTokens ?? 0,
                request.Usage?.OutputTokens ?? 0,
                request.Usage?.CacheTokens ?? 0),
            request.Status,
            ParseTimestamp(request.StartedAtUtc),
            ParseTimestamp(request.CompletedAtUtc),
            string.IsNullOrEmpty(request.ProviderName) ? null : request.ProviderName,
            string.IsNullOrEmpty(request.ProviderAccount) ? null : request.ProviderAccount);

        var result = await billing.RecordUsageAsync(usageEvent, DateTimeOffset.UtcNow, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new RecordUsageResponse
        {
            Accepted = result.Accepted,
            RequestId = result.RequestId ?? string.Empty,
            ErrorCode = result.ErrorCode ?? string.Empty
        };
    }

    public override async Task<GetBalanceResponse> GetBalance(
        GetBalanceRequest request,
        ServerCallContext context)
    {
        try
        {
            var balance = await billing.GetBalanceAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new GetBalanceResponse
            {
                UserId = request.UserId,
                BalanceUsd = balance.Amount.ToString(CultureInfo.InvariantCulture)
            };
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "GetBalance failed. UserId: {UserId}", request.UserId);
            return new GetBalanceResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<CreateVirtualTopUpResponse> CreateVirtualTopUp(
        CreateVirtualTopUpRequest request,
        ServerCallContext context)
    {
        try
        {
            var amount = MoneyAmount.Usd(decimal.Parse(request.AmountUsd, CultureInfo.InvariantCulture));
            var entry = await billing.CreateVirtualTopUpAsync(
                request.AdminId,
                request.UserId,
                amount,
                request.Reason,
                request.IdempotencyKey,
                DateTimeOffset.UtcNow,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            var balance = await billing.GetBalanceAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new CreateVirtualTopUpResponse
            {
                Created = true,
                LedgerEntryId = entry.Id,
                UserId = entry.UserId,
                AmountUsd = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                BalanceUsd = balance.Amount.ToString(CultureInfo.InvariantCulture)
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger.LogWarning(ex, "CreateVirtualTopUp failed. AdminId: {AdminId}; UserId: {UserId}; IdempotencyKey: {IdempotencyKey}", request.AdminId, request.UserId, request.IdempotencyKey);
            return new CreateVirtualTopUpResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<UpsertModelPriceResponse> UpsertModelPrice(
        UpsertModelPriceRequest request,
        ServerCallContext context)
    {
        try
        {
            await billing.UpsertModelAsync(new ModelPrice(
                request.ModelId,
                request.DisplayName,
                request.UpstreamModelId,
                request.PriceVersion,
                new ModelTokenPrice(
                    MoneyAmount.Usd(decimal.Parse(request.InputPricePer1MUsd, CultureInfo.InvariantCulture)),
                    MoneyAmount.Usd(decimal.Parse(request.OutputPricePer1MUsd, CultureInfo.InvariantCulture)),
                    MoneyAmount.Usd(decimal.Parse(request.CachePricePer1MUsd, CultureInfo.InvariantCulture))),
                request.Enabled,
                Description: string.IsNullOrEmpty(request.Description) ? null : request.Description,
                ContextLength: request.ContextLength == 0 ? null : (int?)request.ContextLength,
                Modalities: string.IsNullOrEmpty(request.Modalities) ? null : request.Modalities,
                DescriptionSource: string.IsNullOrEmpty(request.DescriptionSource) ? null : request.DescriptionSource,
                TagIds: request.TagIds.Count > 0 ? request.TagIds.ToList() : null,
                Provider: string.IsNullOrEmpty(request.Provider) ? null : request.Provider,
                SupportsReasoning: request.SupportsReasoning,
                AllowedReasoningEfforts: request.AllowedReasoningEfforts.Count > 0 ? request.AllowedReasoningEfforts.ToList() : null,
                DefaultReasoningEffort: string.IsNullOrEmpty(request.DefaultReasoningEffort) ? null : request.DefaultReasoningEffort),
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new UpsertModelPriceResponse
            {
                Upserted = true,
                ModelId = request.ModelId
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger.LogWarning(ex, "UpsertModelPrice failed. ModelId: {ModelId}; PriceVersion: {PriceVersion}", request.ModelId, request.PriceVersion);
            return new UpsertModelPriceResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<ListBillingAnomaliesResponse> ListBillingAnomalies(
        ListBillingAnomaliesRequest request,
        ServerCallContext context)
    {
        var response = new ListBillingAnomaliesResponse();
        var cursor = string.IsNullOrWhiteSpace(request.AfterCursor) ? null : request.AfterCursor;
        var (items, hasMore) = await billing.ListAnomaliesAsync(cursor, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        
        response.HasMore = hasMore;
        if (hasMore && items.Count > 0)
        {
            var last = items.Last();
            response.NextCursor = $"{last.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{last.Id}";
        }

        response.Anomalies.AddRange(
            items.Select(anomaly => new Contracts.Grpc.Billing.V1.BillingAnomaly
            {
                Id = anomaly.Id,
                RequestId = anomaly.RequestId,
                Type = anomaly.Type,
                Message = anomaly.Message,
                CreatedAtUtc = anomaly.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }));
        return response;
    }

    public override async Task<ListModelsResponse> ListModels(
        ListModelsRequest request,
        ServerCallContext context)
    {
        var response = new ListModelsResponse();
        response.Models.AddRange(
            (await billing.ListAllModelsAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(model =>
            {
                var m = new Contracts.Grpc.Billing.V1.ModelDetail
                {
                    Id = model.ModelId,
                    DisplayName = model.DisplayName,
                    UpstreamModelId = model.RouterModelId,
                    PriceVersion = model.PriceVersion,
                    InputPricePer1MUsd = model.Price.InputPerMillion.Amount.ToString(CultureInfo.InvariantCulture),
                    OutputPricePer1MUsd = model.Price.OutputPerMillion.Amount.ToString(CultureInfo.InvariantCulture),
                    CachePricePer1MUsd = model.Price.CachePerMillion.Amount.ToString(CultureInfo.InvariantCulture),
                    Enabled = model.Enabled,
                    Description = model.Description ?? string.Empty,
                    ContextLength = model.ContextLength ?? 0,
                    Modalities = model.Modalities ?? string.Empty,
                    DescriptionSource = model.DescriptionSource ?? string.Empty,
                    Provider = model.Provider ?? string.Empty,
                    SupportsReasoning = model.SupportsReasoning,
                    DefaultReasoningEffort = model.DefaultReasoningEffort ?? string.Empty,
                };
                if (model.AllowedReasoningEfforts is not null)
                {
                    m.AllowedReasoningEfforts.AddRange(model.AllowedReasoningEfforts);
                }
                if (model.TagIds != null)
                {
                    m.TagIds.AddRange(model.TagIds);
                }
                return m;
            }));
        return response;
    }

    public override async Task<ListUsageResponse> ListUsage(
        ListUsageRequest request,
        ServerCallContext context)
    {
        var response = new ListUsageResponse();
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId;
        var cursor = string.IsNullOrWhiteSpace(request.AfterCursor) ? null : request.AfterCursor;
        var (items, hasMore) = await billing.ListUsageAsync(userId, cursor, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        
        response.HasMore = hasMore;
        if (hasMore && items.Count > 0)
        {
            var last = items.Last();
            response.NextCursor = $"{last.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{last.RequestId}";
        }

        response.Records.AddRange(
            items.Select(usage => new Contracts.Grpc.Billing.V1.UsageRecord
            {
                RequestId = usage.RequestId,
                UserId = usage.UserId,
                ApiKeyId = usage.ApiKeyId,
                ModelId = usage.ModelId,
                PriceVersion = usage.PriceVersion,
                RouterNodeId = usage.RouterNodeId,
                Usage = new Contracts.Grpc.Billing.V1.TokenUsage
                {
                    InputTokens = usage.Usage.InputTokens,
                    OutputTokens = usage.Usage.OutputTokens,
                    CacheTokens = usage.Usage.CacheTokens
                },
                CostUsd = usage.Cost.Amount.ToString(CultureInfo.InvariantCulture),
                Status = usage.Status,
                CreatedAtUtc = usage.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }));
        return response;
    }

    public override async Task<ListActivityByTokenResponse> ListActivityByToken(
        ListActivityByTokenRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var (items, hasMore) = await billing.ListActivityByTokenAsync(
            request.ApiKeyIds.ToArray(),
            fromUtc: ParseOptionalTimestamp(request.FromUtc),
            toUtc: ParseOptionalTimestamp(request.ToUtc),
            modelIdFilter: string.IsNullOrEmpty(request.ModelIdFilter) ? null : request.ModelIdFilter,
            apiKeyIdFilter: string.IsNullOrEmpty(request.ApiKeyIdFilter) ? null : request.ApiKeyIdFilter,
            afterCursor: string.IsNullOrEmpty(request.AfterCursor) ? null : request.AfterCursor,
            pageSize: request.PageSize > 0 ? request.PageSize : null,
            ct: ct).ConfigureAwait(false);

        var response = new ListActivityByTokenResponse { HasMore = hasMore };
        if (hasMore && items.Count > 0)
        {
            var last = items.Last();
            response.NextCursor = $"{last.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{last.RequestId}";
        }

        response.Records.AddRange(items.Select(activity => new Contracts.Grpc.Billing.V1.ActivityByTokenRecord
        {
            RequestId = activity.RequestId,
            ApiKeyId = activity.ApiKeyId,
            ApiKeyPrefix = activity.ApiKeyPrefix,
            ModelId = activity.ModelId,
            RouterNodeId = activity.RouterNodeId,
            Usage = new Contracts.Grpc.Billing.V1.TokenUsage
            {
                InputTokens = activity.Usage.InputTokens,
                OutputTokens = activity.Usage.OutputTokens,
                CacheTokens = activity.Usage.CacheTokens
            },
            Status = activity.Status,
            CreatedAtUtc = activity.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CompletedAtUtc = activity.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CostUsd = activity.Cost.Amount.ToString(CultureInfo.InvariantCulture),
            ProviderName = activity.ProviderName ?? "",
            ProviderAccount = activity.ProviderAccount ?? ""
        }));
        return response;
    }

    public override async Task<GetActivitySummaryResponse> GetActivitySummary(
        GetActivitySummaryRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var summary = await billing.GetActivitySummaryAsync(
            request.ApiKeyIds.ToArray(),
            fromUtc: ParseOptionalTimestamp(request.FromUtc),
            toUtc: ParseOptionalTimestamp(request.ToUtc),
            ct: ct).ConfigureAwait(false);

        var response = new GetActivitySummaryResponse
        {
            TotalRequests = summary.TotalRequests,
            TotalInputTokens = summary.TotalInputTokens,
            TotalOutputTokens = summary.TotalOutputTokens,
            TotalCacheTokens = summary.TotalCacheTokens,
            TotalCostUsd = summary.TotalCost.Amount.ToString(CultureInfo.InvariantCulture)
        };
        response.Models.AddRange(summary.Models.Select(m => new Contracts.Grpc.Billing.V1.ActivityModelSummary
        {
            ModelId = m.ModelId,
            Requests = m.Requests,
            InputTokens = m.InputTokens,
            OutputTokens = m.OutputTokens,
            CacheTokens = m.CacheTokens,
            CostUsd = m.Cost.Amount.ToString(CultureInfo.InvariantCulture)
        }));
        response.Keys.AddRange(summary.Keys.Select(k => new Contracts.Grpc.Billing.V1.ActivityKeySummary
        {
            ApiKeyId = k.ApiKeyId,
            ApiKeyPrefix = k.ApiKeyPrefix,
            Requests = k.Requests,
            InputTokens = k.InputTokens,
            OutputTokens = k.OutputTokens,
            CacheTokens = k.CacheTokens,
            CostUsd = k.Cost.Amount.ToString(CultureInfo.InvariantCulture)
        }));
        return response;
    }

    public override async Task<ListTopUpsResponse> ListTopUps(
        ListTopUpsRequest request,
        ServerCallContext context)
    {
        var response = new ListTopUpsResponse();
        response.Records.AddRange(
            (await billing.ListTopUpsAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(entry => new TopUpRecord
            {
                Id = entry.Id,
                AmountUsd = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                Reason = entry.Reason,
                CreatedAtUtc = entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }));
        return response;
    }

    public override async Task<ListAdminRequestsResponse> ListAdminRequests(
        ListAdminRequestsRequest request,
        ServerCallContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var (items, hasMore) = await billing.GetAdminUsageRecordsAsync(
            pageSize,
            afterCursor: string.IsNullOrEmpty(request.AfterCursor) ? null : request.AfterCursor,
            modelFilter: string.IsNullOrEmpty(request.ModelFilter) ? null : request.ModelFilter,
            providerFilter: string.IsNullOrEmpty(request.ProviderFilter) ? null : request.ProviderFilter,
            fromUtc: ParseOptionalTimestamp(request.FromUtc),
            toUtc: ParseOptionalTimestamp(request.ToUtc),
            ct: ct).ConfigureAwait(false);

        var response = new ListAdminRequestsResponse();
        if (hasMore && items.Count > 0)
        {
            var last = items.Last();
            response.NextCursor = $"{last.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{last.RequestId}";
        }

        response.Records.AddRange(items.Select(r => new Contracts.Grpc.Billing.V1.AdminRequestRecord
        {
            RequestId = r.RequestId,
            UserId = r.UserId,
            ApiKeyPrefix = r.ApiKeyPrefix,
            ModelId = r.ModelId,
            RouterNodeId = r.RouterNodeId,
            ProviderName = r.ProviderName ?? "",
            ProviderAccount = r.ProviderAccount ?? "",
            InputTokens = r.Usage.InputTokens,
            OutputTokens = r.Usage.OutputTokens,
            CacheTokens = r.Usage.CacheTokens,
            CostUsd = r.Cost.Amount.ToString(CultureInfo.InvariantCulture),
            Status = r.Status,
            CreatedAtUtc = r.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CompletedAtUtc = r.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }));
        return response;
    }

    public override async Task<ListTagsResponse> ListTags(
        ListTagsRequest request,
        ServerCallContext context)
    {
        var response = new ListTagsResponse();
        response.Tags.AddRange(
            (await billing.ListTagsAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false))
            .Select(t => new Contracts.Grpc.Billing.V1.Tag
            {
                Id = t.Id,
                DisplayName = t.DisplayName,
                Color = t.Color ?? string.Empty
            }));
        return response;
    }

    public override async Task<UpsertTagResponse> UpsertTag(
        UpsertTagRequest request,
        ServerCallContext context)
    {
        try
        {
            await billing.UpsertTagAsync(
                new OpenRussiaRouter.BillingService.Domain.Entities.Tag(
                    request.Id,
                    request.DisplayName,
                    string.IsNullOrEmpty(request.Color) ? null : request.Color),
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new UpsertTagResponse { Upserted = true, Id = request.Id };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "UpsertTag failed. Id: {Id}", request.Id);
            return new UpsertTagResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<DeleteTagResponse> DeleteTag(
        DeleteTagRequest request,
        ServerCallContext context)
    {
        try
        {
            await billing.DeleteTagAsync(request.Id, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new DeleteTagResponse { Deleted = true };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DeleteTag failed. Id: {Id}", request.Id);
            return new DeleteTagResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<GetPlatformSettingResponse> GetPlatformSetting(
        GetPlatformSettingRequest request,
        ServerCallContext context)
    {
        var value = await billing.GetPlatformSettingAsync(
            request.Key, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        return new GetPlatformSettingResponse { Value = value ?? string.Empty, Found = value != null };
    }

    public override async Task<SetPlatformSettingResponse> SetPlatformSetting(
        SetPlatformSettingRequest request,
        ServerCallContext context)
    {
        try
        {
            await billing.SetPlatformSettingAsync(
                request.Key, request.Value, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new SetPlatformSettingResponse { Ok = true };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SetPlatformSetting failed. Key: {Key}", request.Key);
            return new SetPlatformSettingResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override Task<GetFxQuoteResponse> GetFxQuote(GetFxQuoteRequest request, ServerCallContext context)
    {
        try
        {
            var quote = billing.GetFxQuote(request.ProviderCurrency, DateTimeOffset.UtcNow);
            return Task.FromResult(new GetFxQuoteResponse
            {
                Rate = quote.Rate.ToString(CultureInfo.InvariantCulture),
                UsdPerUnit = quote.UsdPerUnit.ToString(CultureInfo.InvariantCulture),
                FxSource = quote.Source,
                FxAt = quote.FxAtUtc.ToString("O", CultureInfo.InvariantCulture),
                QuoteId = quote.QuoteId,
                ExpiresAt = quote.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            });
        }
        catch (FxQuoteException ex)
        {
            return Task.FromResult(new GetFxQuoteResponse { ErrorCode = ex.ErrorCode });
        }
    }

    public override async Task<CreatePaymentResponse> CreatePayment(CreatePaymentRequest request, ServerCallContext context)
    {
        try
        {
            var payment = await billing.CreatePaymentAsync(
                request.UserId, request.Provider,
                decimal.Parse(request.ProviderAmount, CultureInfo.InvariantCulture), request.ProviderCurrency,
                decimal.Parse(request.DisplayAmount, CultureInfo.InvariantCulture), request.DisplayCurrency,
                request.ClientToken, DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            return new CreatePaymentResponse
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CreditedUsdAmount = payment.CreditedUsd.Amount.ToString(CultureInfo.InvariantCulture),
                FxRate = payment.FxRate.ToString(CultureInfo.InvariantCulture)
            };
        }
        catch (FxQuoteException ex)
        {
            return new CreatePaymentResponse { ErrorCode = ex.ErrorCode };
        }
        catch (PaymentOperationException ex)
        {
            return new CreatePaymentResponse { ErrorCode = ex.ErrorCode };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            logger.LogWarning(ex, "CreatePayment validation failed for user {UserId}.", request.UserId);
            return new CreatePaymentResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<AttachProviderRefResponse> AttachProviderRef(AttachProviderRefRequest request, ServerCallContext context)
    {
        try
        {
            await billing.AttachProviderRefAsync(request.PaymentId, request.ProviderPaymentId,
                string.IsNullOrEmpty(request.PayUrl) ? null : request.PayUrl,
                ParseOptionalTimestamp(request.ExpiresAtUtc), context.CancellationToken).ConfigureAwait(false);
            return new AttachProviderRefResponse { Ok = true };
        }
        catch (PaymentOperationException ex)
        {
            return new AttachProviderRefResponse { ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ConfirmPaymentByProviderResponse> ConfirmPaymentByProvider(ConfirmPaymentByProviderRequest request, ServerCallContext context)
    {
        if (!request.SignatureOk)
            return new ConfirmPaymentByProviderResponse { ErrorCode = ErrorCodes.PaymentProviderUnavailable };
        try
        {
            var outcome = await billing.ConfirmPaymentByProviderAsync(new PaymentEventInput(
                request.Provider, EmptyToNull(request.ProviderPaymentId), EmptyToNull(request.OrderId), "paid",
                decimal.Parse(request.ConfirmedAmount, CultureInfo.InvariantCulture), request.ConfirmedCurrency,
                null, EmptyToNull(request.ProviderEventId), EmptyToNull(request.RawPayload), request.SignatureOk),
                DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
            return new ConfirmPaymentByProviderResponse
            {
                Confirmed = outcome.Confirmed,
                LedgerEntryId = outcome.LedgerEntryId ?? "",
                BalanceUsd = outcome.BalanceUsd.ToString(CultureInfo.InvariantCulture),
                ErrorCode = outcome.ErrorCode ?? ""
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            return new ConfirmPaymentByProviderResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<ApplyPaymentEventResponse> ApplyPaymentEvent(ApplyPaymentEventRequest request, ServerCallContext context)
    {
        if (!request.SignatureOk)
            return new ApplyPaymentEventResponse { ErrorCode = ErrorCodes.PaymentProviderUnavailable };
        decimal? amount = string.IsNullOrEmpty(request.ConfirmedAmount)
            ? null : decimal.Parse(request.ConfirmedAmount, CultureInfo.InvariantCulture);
        var outcome = await billing.ApplyPaymentEventAsync(new PaymentEventInput(
            request.Provider, EmptyToNull(request.ProviderPaymentId), EmptyToNull(request.OrderId), request.EventType,
            amount, EmptyToNull(request.ConfirmedCurrency), EmptyToNull(request.Reason),
            EmptyToNull(request.ProviderEventId), EmptyToNull(request.RawPayload), request.SignatureOk),
            DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
        return new ApplyPaymentEventResponse
        {
            Applied = outcome.Applied,
            LedgerEntryId = outcome.LedgerEntryId ?? "",
            ErrorCode = outcome.ErrorCode ?? ""
        };
    }

    public override async Task<Contracts.Grpc.Billing.V1.PaymentRecord> GetPayment(GetPaymentRequest request, ServerCallContext context)
    {
        try
        {
            return ToGrpcPayment(await billing.GetPaymentAsync(request.PaymentId, context.CancellationToken).ConfigureAwait(false));
        }
        catch (PaymentOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.ErrorCode));
        }
    }

    public override async Task<ListPaymentsResponse> ListPayments(ListPaymentsRequest request, ServerCallContext context)
    {
        var response = new ListPaymentsResponse();
        response.Records.AddRange((await billing.ListPaymentsAsync(request.UserId,
            request.PageSize > 0 ? request.PageSize : null, context.CancellationToken).ConfigureAwait(false)).Select(ToGrpcPayment));
        return response;
    }

    public override async Task<AcquireStalePaymentsResponse> AcquireStalePayments(AcquireStalePaymentsRequest request, ServerCallContext context)
    {
        var olderThan = ParseOptionalTimestamp(request.OlderThanUtc) ?? DateTimeOffset.UtcNow.AddMinutes(-30);
        var lease = TimeSpan.FromSeconds(request.LeaseSeconds > 0 ? request.LeaseSeconds : 60);
        var limit = request.Limit > 0 ? request.Limit : 10;
        var payments = await billing.AcquireStalePaymentsAsync(
            request.WorkerId, lease, olderThan, limit, context.CancellationToken).ConfigureAwait(false);
        var response = new AcquireStalePaymentsResponse();
        response.Records.AddRange(payments.Select(ToGrpcPayment));
        return response;
    }

    private static Contracts.Grpc.Billing.V1.PaymentRecord ToGrpcPayment(Payment payment) => new()
    {
        Id = payment.Id,
        UserId = payment.UserId,
        Provider = payment.Provider,
        ProviderPaymentId = payment.ProviderPaymentId ?? "",
        OrderId = payment.OrderId,
        DisplayAmount = payment.DisplayAmount.ToString(CultureInfo.InvariantCulture),
        DisplayCurrency = payment.DisplayCurrency,
        ProviderAmount = payment.ProviderAmount.ToString(CultureInfo.InvariantCulture),
        ProviderCurrency = payment.ProviderCurrency,
        CreditedUsdAmount = payment.CreditedUsd.Amount.ToString(CultureInfo.InvariantCulture),
        FxRate = payment.FxRate.ToString(CultureInfo.InvariantCulture),
        FxSource = payment.FxSource,
        FxAtUtc = payment.FxAtUtc.ToString("O", CultureInfo.InvariantCulture),
        Status = payment.Status.ToString(),
        CancelReason = payment.CancelReason ?? "",
        CreatedAtUtc = payment.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ExpiresAtUtc = payment.ExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
        ConfirmedAtUtc = payment.ConfirmedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
        CanceledAtUtc = payment.CanceledAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""
    };

    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

    private DateTimeOffset ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();

        logger.LogWarning("Failed to parse timestamp '{Value}'; using UtcNow as fallback.", value);
        return DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? ParseOptionalTimestamp(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
