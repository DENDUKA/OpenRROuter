using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.WebApp.Services.Seo;

/// <summary>
/// Builds schema.org JSON-LD blocks for the public pages. All absolute URLs must come from
/// <see cref="SiteBaseUrlResolver"/> — never from <see cref="ApiBaseUrlResolver"/>, which points at
/// the api subdomain and must not leak into crawler-facing metadata.
/// Values are serialized with the default <c>System.Text.Json</c> encoder, which escapes
/// <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>, so the output is safe inside a
/// <c>&lt;script type="application/ld+json"&gt;</c> element even when it carries catalog text the
/// platform does not control.
/// </summary>
public static class StructuredData
{
    private const string Context = "https://schema.org";
    private const string TokenReferenceQuantity = "1000000";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Site-wide <c>Organization</c> + <c>WebSite</c> graph, emitted once per indexable page from
    /// the layout. <paramref name="baseUrl"/> is the site origin without a trailing slash.
    /// </summary>
    public static string SiteGraph(string baseUrl, string language)
    {
        var organization = new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["@id"] = baseUrl + "/#organization",
            ["name"] = "OpenRussiaRouter",
            ["url"] = baseUrl + "/",
            ["logo"] = baseUrl + "/og-image.png",
        };

        var website = new Dictionary<string, object?>
        {
            ["@type"] = "WebSite",
            ["@id"] = baseUrl + "/#website",
            ["name"] = "OpenRussiaRouter",
            ["url"] = baseUrl + "/",
            ["inLanguage"] = language,
            ["publisher"] = new Dictionary<string, object?> { ["@id"] = baseUrl + "/#organization" },
        };

        return Serialize(new Dictionary<string, object?>
        {
            ["@context"] = Context,
            ["@graph"] = new object[] { organization, website },
        });
    }

    /// <summary>
    /// <c>BreadcrumbList</c> for a detail page. <paramref name="items"/> runs from the root crumb to
    /// the current page; every URL must be absolute.
    /// </summary>
    public static string Breadcrumbs(IReadOnlyList<(string Name, string Url)> items)
    {
        var elements = items
            .Select((item, index) => (object)new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = index + 1,
                ["name"] = item.Name,
                ["item"] = item.Url,
            })
            .ToArray();

        return Serialize(new Dictionary<string, object?>
        {
            ["@context"] = Context,
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = elements,
        });
    }

    /// <summary>
    /// <c>Product</c> for a model detail page. Token prices are modelled as
    /// <c>UnitPriceSpecification</c> per 1M tokens — the unit the catalog quotes — with the input
    /// price also surfaced as the headline <c>Offer.price</c>. Pass <paramref name="hasPricing"/> as
    /// <c>false</c> for models with no price so the offer is omitted rather than advertising a free
    /// product.
    /// </summary>
    public static string ModelProduct(
        string name,
        string description,
        string modelId,
        string providerName,
        string canonicalUrl,
        decimal inputPricePer1M,
        decimal outputPricePer1M,
        bool hasPricing)
    {
        var product = new Dictionary<string, object?>
        {
            ["@context"] = Context,
            ["@type"] = "Product",
            ["name"] = name,
            ["description"] = description,
            ["sku"] = modelId,
            ["url"] = canonicalUrl,
            ["category"] = "AI language model",
            ["brand"] = new Dictionary<string, object?>
            {
                ["@type"] = "Brand",
                ["name"] = providerName,
            },
        };

        if (hasPricing)
        {
            product["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["url"] = canonicalUrl,
                ["priceCurrency"] = "USD",
                ["price"] = Money(inputPricePer1M),
                ["availability"] = "https://schema.org/InStock",
                ["priceSpecification"] = new object[]
                {
                    TokenPrice("Input tokens", inputPricePer1M),
                    TokenPrice("Output tokens", outputPricePer1M),
                },
            };
        }

        return Serialize(product);
    }

    private static Dictionary<string, object?> TokenPrice(string name, decimal pricePer1M) => new()
    {
        ["@type"] = "UnitPriceSpecification",
        ["name"] = name,
        ["price"] = Money(pricePer1M),
        ["priceCurrency"] = "USD",
        ["referenceQuantity"] = new Dictionary<string, object?>
        {
            ["@type"] = "QuantitativeValue",
            ["value"] = TokenReferenceQuantity,
            ["unitText"] = "tokens",
        },
    };

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
}
