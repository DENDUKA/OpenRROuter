using System.Globalization;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Content;

namespace OpenRussiaRouter.WebApp.Pages;

public class ToolsModel(
    ContentProvider contentProvider,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ToolsModel> logger) : PageModelBase
{
    public IReadOnlyList<ToolDisplayInfo> Tools { get; private set; } = [];
    public IReadOnlyList<CategoryFilter> CategoryFilters { get; private set; } = [];
    public string? SelectedCategoryId { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string? category)
    {
        SelectedCategoryId = string.IsNullOrWhiteSpace(category) ? null : category;

        try
        {
            var toolsTask = contentProvider.ListToolsAsync(SelectedCategoryId, HttpContext.RequestAborted);
            var categoriesTask = contentProvider.ListToolCategoriesAsync(HttpContext.RequestAborted);
            await Task.WhenAll(toolsTask, categoriesTask);

            Tools = toolsTask.Result
                .Select(t => new ToolDisplayInfo(
                    t.Id,
                    t.DisplayName,
                    PickLocalized(t.TaglineRu, t.TaglineEn),
                    t.Tags,
                    $"/tools/logo/{Uri.EscapeDataString(t.Id)}?v={t.UpdatedAtUtc.UtcDateTime.Ticks}"))
                .ToList();

            CategoryFilters = categoriesTask.Result
                .Select(c => new CategoryFilter(c.Id, IsRussian ? c.NameRu : c.NameEn))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tools list.");
            ErrorMessage = localizer["Tools_LoadFailed"];
        }
    }

    private static bool IsRussian =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    /// <summary>Prefers the current culture, falling back to the other so a half-filled tool still reads.</summary>
    private static string? PickLocalized(string? ru, string? en)
    {
        var preferred = IsRussian ? ru : en;
        var fallback = IsRussian ? en : ru;
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }

    public sealed record ToolDisplayInfo(
        string Id,
        string DisplayName,
        string? Tagline,
        IReadOnlyList<string> Tags,
        string LogoUrl);

    public sealed record CategoryFilter(string Id, string Name);
}
