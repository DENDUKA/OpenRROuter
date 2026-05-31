using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelDetailsModel : PageModelBase
{
    private readonly BillingProvider _billingClient;

    public ModelDetailsModel(BillingProvider billingProvider)
    {
        _billingClient = billingProvider;
    }

    [BindProperty(SupportsGet = true)]
    public string Provider { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = "";

    public string ModelId => $"{Provider}/{Slug}";

    public ModelInfo? ModelData { get; private set; }

    public string DisplayName => ModelData?.DisplayName ?? ToTitle(Slug.Replace('-', ' '));
    public string ProviderTitle => ToTitle(Provider);
    public string Initials => string.Concat(
        ProviderTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Take(2)
                     .Select(p => char.ToUpperInvariant(p[0])));

    public string? Description => ModelData?.Description;
    public string ContextDisplay => ModelData?.ContextLength is > 0
        ? FormatContext(ModelData.ContextLength.Value)
        : "Unknown";
    public string ModalitiesDisplay => ModelData?.Modalities ?? "Text";
    public decimal InputPrice => ModelData?.InputPrice ?? 0;
    public decimal OutputPrice => ModelData?.OutputPrice ?? 0;
    public decimal CachePrice => ModelData?.CachePrice ?? 0;
    public bool HasPricing => InputPrice > 0 || OutputPrice > 0;

    public async Task OnGetAsync()
    {
        try
        {
            var models = await _billingClient.ListModelsAsync();

            ModelData = models.FirstOrDefault(m =>
                string.Equals(m.Id, ModelId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.UpstreamModelId, ModelId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
        }
    }

    private static string FormatContext(int tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000M:0.#}M"
        : tokens >= 1_000 ? $"{tokens / 1_000}K"
        : tokens.ToString();

    private static string ToTitle(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}
