using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Enrichment;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

/// <summary>
/// Side-by-side comparison of our selling prices with OpenRouter's list prices.
/// Read-only: nothing here writes to the billing catalog.
/// </summary>
[Authorize(Roles = "Admin")]
public class ModelPricesModel(
    BillingProvider billingProvider,
    IOpenRouterCatalogClient openRouterClient,
    ILogger<ModelPricesModel> logger) : PageModelBase
{
    public IReadOnlyList<PriceComparisonRow> Rows { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool CatalogUnavailable { get; private set; }

    /// <summary>Models returned by Billing before filtering — shown so an empty table is always explainable.</summary>
    public int BillingModelCount { get; private set; }

    /// <summary>Entries in the OpenRouter catalog (collector cache or direct fetch).</summary>
    public int CatalogEntryCount { get; private set; }

    /// <summary>Show only enabled models (query <c>?enabledOnly=true</c>).</summary>
    [BindProperty(SupportsGet = true, Name = "enabledOnly")]
    public bool EnabledOnly { get; set; }

    /// <summary>Sort key: <c>diff</c> (default), <c>id</c>, <c>name</c>, <c>price</c>.</summary>
    [BindProperty(SupportsGet = true, Name = "sort")]
    public string Sort { get; set; } = "diff";

    public int CheaperCount => Rows.Count(r => r.TotalDiffPercent is < 0);
    public int PricierCount => Rows.Count(r => r.TotalDiffPercent is > 0);
    public int EqualCount => Rows.Count(r => r.TotalDiffPercent == 0);
    public int UnmatchedCount => Rows.Count(r => !r.Matched);

    public async Task OnGetAsync()
    {
        IReadOnlyList<ModelInfo> models = [];
        try
        {
            models = await billingProvider.ListModelsAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load billing catalog for price comparison");
            ErrorMessage = "Billing catalog unavailable.";
            return;
        }

        IReadOnlyList<OpenRouterModelEntry> catalog = [];
        try
        {
            catalog = await openRouterClient.LoadCatalogAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load OpenRouter catalog for price comparison");
        }

        BillingModelCount = models.Count;
        CatalogEntryCount = catalog.Count;

        logger.LogInformation(
            "Price comparison page loaded: BillingModels={BillingCount}, OpenRouterCatalogEntries={CatalogCount}, EnabledOnly={EnabledOnly}",
            BillingModelCount, CatalogEntryCount, EnabledOnly);

        if (catalog.Count == 0)
        {
            CatalogUnavailable = true;
            ErrorMessage ??= "OpenRouter catalog is empty or unreachable — only our own prices are shown.";
        }

        var source = EnabledOnly ? models.Where(m => m.Enabled) : models;

        var rows = source
            .Select(model =>
            {
                var match = catalog.Count > 0 ? openRouterClient.FindMatch(catalog, model.UpstreamModelId) : null;
                return new PriceComparisonRow(
                    model.Id,
                    model.DisplayName,
                    model.UpstreamModelId,
                    model.Provider,
                    model.Enabled,
                    model.InputPrice,
                    model.OutputPrice,
                    model.CachePrice,
                    match?.MatchedId,
                    match?.InputPrice,
                    match?.OutputPrice,
                    match?.CachePrice);
            })
            .ToList();

        Rows = Sort switch
        {
            // Unmatched rows have no delta — always park them at the bottom.
            "diff" => rows
                .OrderBy(r => r.TotalDiffPercent is null)
                .ThenByDescending(r => r.TotalDiffPercent ?? 0m)
                .ThenBy(r => r.ModelId, StringComparer.Ordinal)
                .ToList(),
            "name" => rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            "price" => rows
                .OrderByDescending(r => r.OurInput + r.OurOutput)
                .ThenBy(r => r.ModelId, StringComparer.Ordinal)
                .ToList(),
            _ => rows.OrderBy(r => r.ModelId, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>Prices are $ per 1M tokens on both sides; <c>Or*</c> is <c>null</c> when the model has no OpenRouter counterpart.</summary>
    public sealed record PriceComparisonRow(
        string ModelId,
        string DisplayName,
        string RouterModelId,
        string? Provider,
        bool Enabled,
        decimal OurInput,
        decimal OurOutput,
        decimal OurCache,
        string? MatchedOpenRouterId,
        decimal? OrInput,
        decimal? OrOutput,
        decimal? OrCache)
    {
        public bool Matched => MatchedOpenRouterId is not null;

        public decimal? InputDiffPercent => DiffPercent(OurInput, OrInput);
        public decimal? OutputDiffPercent => DiffPercent(OurOutput, OrOutput);
        public decimal? CacheDiffPercent => DiffPercent(OurCache, OrCache);

        /// <summary>Overall verdict: input+output on our side vs input+output on theirs.</summary>
        public decimal? TotalDiffPercent =>
            OrInput is null && OrOutput is null
                ? null
                : DiffPercent(OurInput + OurOutput, (OrInput ?? 0m) + (OrOutput ?? 0m));

        /// <summary>Signed percentage: negative = we are cheaper, positive = we are pricier. <c>null</c> when not comparable.</summary>
        private static decimal? DiffPercent(decimal ours, decimal? theirs)
        {
            if (theirs is not { } their)
                return null;
            if (their == 0m)
                return ours == 0m ? 0m : null; // free upstream — a ratio would be meaningless.
            return decimal.Round((ours - their) / their * 100m, 1);
        }
    }
}
