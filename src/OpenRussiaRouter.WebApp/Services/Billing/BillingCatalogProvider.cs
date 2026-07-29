using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>
/// gRPC wrapper for model catalog, providers, and tags operations.
/// Extracted from <see cref="BillingProvider"/> (T-061).
/// </summary>
internal sealed class BillingCatalogProvider(BillingGrpc.BillingServiceClient client)
{
    public async Task<IReadOnlyList<ModelInfo>> ListPublicModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListPublicModelsAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListPublicModelsRequest(), cancellationToken: cancellationToken);
        return response.Models.Select(m => new ModelInfo(
            m.Id,
            m.DisplayName,
            m.UpstreamModelId,
            "",
            ParseDecimal(m.InputPricePer1MUsd),
            ParseDecimal(m.OutputPricePer1MUsd),
            ParseDecimal(m.CachePricePer1MUsd),
            true,
            DescriptionRu: string.IsNullOrEmpty(m.DescriptionRu) ? null : m.DescriptionRu,
            DescriptionEn: string.IsNullOrEmpty(m.DescriptionEn) ? null : m.DescriptionEn,
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
            ParseDecimal(m.InputPricePer1MUsd),
            ParseDecimal(m.OutputPricePer1MUsd),
            ParseDecimal(m.CachePricePer1MUsd),
            m.Enabled,
            DescriptionRu: string.IsNullOrEmpty(m.DescriptionRu) ? null : m.DescriptionRu,
            DescriptionEn: string.IsNullOrEmpty(m.DescriptionEn) ? null : m.DescriptionEn,
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
            ParseDecimal(m.InputPricePer1MUsd),
            ParseDecimal(m.OutputPricePer1MUsd),
            ParseDecimal(m.CachePricePer1MUsd),
            m.Enabled,
            DescriptionRu: string.IsNullOrEmpty(m.DescriptionRu) ? null : m.DescriptionRu,
            DescriptionEn: string.IsNullOrEmpty(m.DescriptionEn) ? null : m.DescriptionEn,
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
            InputPricePer1MUsd = model.InputPrice.ToString(CultureInfo.InvariantCulture),
            OutputPricePer1MUsd = model.OutputPrice.ToString(CultureInfo.InvariantCulture),
            CachePricePer1MUsd = model.CachePrice.ToString(CultureInfo.InvariantCulture),
            Enabled = model.Enabled,
            DescriptionRu = model.DescriptionRu ?? string.Empty,
            DescriptionEn = model.DescriptionEn ?? string.Empty,
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
            request.TagIds.AddRange(model.TagIds);
        if (model.AllowedReasoningEfforts != null)
            request.AllowedReasoningEfforts.AddRange(model.AllowedReasoningEfforts);

        var response = await client.UpsertModelPriceAsync(request, cancellationToken: cancellationToken);
        if (!response.Upserted)
            throw new BillingException(response.ErrorCode);
    }

    public async Task<(int UpsertedCount, IReadOnlyList<string> FailedModelIds)> UpsertModelsAsync(
        IReadOnlyList<ModelInfo> models,
        CancellationToken cancellationToken = default)
    {
        var batchRequest = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertModelsRequest();
        foreach (var model in models)
        {
            var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertModelPriceRequest
            {
                ModelId = model.Id,
                DisplayName = model.DisplayName,
                UpstreamModelId = model.UpstreamModelId,
                PriceVersion = model.PriceVersion,
                InputPricePer1MUsd = model.InputPrice.ToString(CultureInfo.InvariantCulture),
                OutputPricePer1MUsd = model.OutputPrice.ToString(CultureInfo.InvariantCulture),
                CachePricePer1MUsd = model.CachePrice.ToString(CultureInfo.InvariantCulture),
                Enabled = model.Enabled,
                DescriptionRu = model.DescriptionRu ?? string.Empty,
                DescriptionEn = model.DescriptionEn ?? string.Empty,
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
                request.TagIds.AddRange(model.TagIds);
            if (model.AllowedReasoningEfforts != null)
                request.AllowedReasoningEfforts.AddRange(model.AllowedReasoningEfforts);
            batchRequest.Models.Add(request);
        }

        var response = await client.UpsertModelsAsync(batchRequest, cancellationToken: cancellationToken);
        return (response.UpsertedCount, response.FailedModelIds.ToList());
    }

    public async Task<IReadOnlyList<ProviderInfo>> ListModelProvidersAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListModelProvidersAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListProvidersRequest { HasLogo = false }, cancellationToken: cancellationToken);
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
            request.LogoBytes = Google.Protobuf.ByteString.CopyFrom(provider.LogoBytes);

        var response = await client.UpsertModelProviderAsync(request, cancellationToken: cancellationToken);
        if (!response.Upserted)
            throw new BillingException(response.ErrorCode);
    }

    public async Task DeleteModelProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteModelProviderAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.DeleteProviderRequest { ProviderId = providerId },
            cancellationToken: cancellationToken);
        if (!response.Deleted)
            throw new BillingException(response.ErrorCode);
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
            throw new BillingException(response.ErrorCode);
    }

    public async Task DeleteTagAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteTagAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.DeleteTagRequest { Id = id }, cancellationToken: cancellationToken);
        if (!response.Deleted)
            throw new BillingException(response.ErrorCode);
    }

    private static decimal ParseDecimal(string value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, CultureInfo.InvariantCulture);
}
