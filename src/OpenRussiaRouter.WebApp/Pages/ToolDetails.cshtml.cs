using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Content;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Pages;

public class ToolDetailsModel(
    ContentProvider contentProvider,
    ApiBaseUrlResolver apiBaseUrlResolver,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ToolDetailsModel> logger) : PageModelBase
{
    /// <summary>
    /// Written by admins into description/setup markdown instead of a literal API host, so guides
    /// follow the admin API base URL setting (no trailing slash, without <c>/v1</c>).
    /// </summary>
    public const string ApiBaseUrlPlaceholder = "{{API_BASE_URL}}";

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = "";

    public ToolInfo? Tool { get; private set; }
    public string? CategoryName { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>User-facing API base URL substituted for <see cref="ApiBaseUrlPlaceholder"/>.</summary>
    public string ApiBaseUrl { get; private set; } = "";

    public string LogoUrl =>
        $"/tools/logo/{Uri.EscapeDataString(Id)}?v={Tool?.UpdatedAtUtc.UtcDateTime.Ticks ?? 0}";

    public string? Tagline => PickLocalized(Tool?.TaglineRu, Tool?.TaglineEn);

    /// <summary>Rendered with the strict pipeline: admin-authored, and only our own images allowed.</summary>
    public string DescriptionHtml =>
        MarkdownRenderer.RenderStrict(SubstituteApiBaseUrl(PickLocalized(Tool?.DescriptionRu, Tool?.DescriptionEn)));

    public string SetupHtml =>
        MarkdownRenderer.RenderStrict(SubstituteApiBaseUrl(PickLocalized(Tool?.SetupRu, Tool?.SetupEn)));

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            // Unpublished tools come back as null — drafts must not be reachable by URL.
            Tool = await contentProvider.GetToolAsync(Id, HttpContext.RequestAborted);

            if (Tool is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                ErrorMessage = localizer["ToolDetails_NotFound"];
                return Page();
            }

            ApiBaseUrl = await apiBaseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);

            if (Tool.CategoryId is not null)
            {
                var categories = await contentProvider.ListToolCategoriesAsync(HttpContext.RequestAborted);
                var category = categories.FirstOrDefault(c => c.Id == Tool.CategoryId);
                CategoryName = category is null ? null : (IsRussian ? category.NameRu : category.NameEn);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tool details. Id={Id}", Id);
            ErrorMessage = localizer["ToolDetails_LoadFailed"];
        }

        return Page();
    }

    private static bool IsRussian =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    private string? SubstituteApiBaseUrl(string? markdown) =>
        markdown?.Replace(ApiBaseUrlPlaceholder, ApiBaseUrl, StringComparison.Ordinal);

    private static string? PickLocalized(string? ru, string? en)
    {
        var preferred = IsRussian ? ru : en;
        var fallback = IsRussian ? en : ru;
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }
}
