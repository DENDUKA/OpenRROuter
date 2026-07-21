using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Content;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

/// <summary>
/// Tool list. Editing lives on its own page (<c>/admin/tools/edit/{id?}</c>) because the editor
/// carries four markdown bodies plus an image library — too much for the inline form pattern
/// used by Tags/Providers.
/// </summary>
[Authorize(Roles = "Admin")]
public class ToolsModel(ContentProvider contentProvider, ILogger<ToolsModel> logger) : PageModelBase
{
    public IReadOnlyList<ToolSummaryInfo> Tools { get; private set; } = [];
    public IReadOnlyDictionary<string, ToolCategoryInfo> Categories { get; private set; } =
        new Dictionary<string, ToolCategoryInfo>();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string success)
        {
            SuccessMessage = success;
        }

        if (TempData["ErrorMessage"] is string error)
        {
            ErrorMessage = error;
        }

        await LoadAsync();
    }

    public async Task<IActionResult> OnPostTogglePublishAsync(string id, bool publish)
    {
        try
        {
            await contentProvider.SetToolPublishedAsync(id, publish, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = publish ? $"Tool '{id}' published." : $"Tool '{id}' unpublished.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to change publish state. Id={Id} Publish={Publish}", id, publish);
            TempData["ErrorMessage"] = $"Failed to update '{id}'.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        try
        {
            await contentProvider.DeleteToolAsync(id, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = $"Tool '{id}' deleted.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tool. Id={Id}", id);
            TempData["ErrorMessage"] = $"Failed to delete '{id}'.";
        }

        return RedirectToPage();
    }

    public string CategoryName(string? categoryId)
    {
        if (categoryId is null)
        {
            return "—";
        }

        return Categories.TryGetValue(categoryId, out var category) ? category.NameEn : categoryId;
    }

    private async Task LoadAsync()
    {
        try
        {
            var tools = contentProvider.ListToolsForAdminAsync(HttpContext.RequestAborted);
            var categories = contentProvider.ListToolCategoriesAsync(HttpContext.RequestAborted);
            await Task.WhenAll(tools, categories);

            Tools = tools.Result;
            Categories = categories.Result.ToDictionary(c => c.Id, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tools list.");
            ErrorMessage ??= "Failed to load tools.";
        }
    }
}
