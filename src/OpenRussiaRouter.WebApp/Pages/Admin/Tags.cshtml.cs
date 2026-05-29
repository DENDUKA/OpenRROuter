using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TagsModel(BillingProvider billingProvider, ILogger<TagsModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly ILogger<TagsModel> _logger = logger;

    public IReadOnlyList<TagInfo> Tags { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? EditId => Request.Query["id"];

    public bool ShowForm { get; set; }
    public bool IsAdd { get; set; }

    [BindProperty] public TagFormInput Form { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string success)
            SuccessMessage = success;

        if (Action == "add")
        {
            ShowForm = true;
            IsAdd = true;
        }
        else if (Action == "edit" && EditId != null)
        {
            ShowForm = true;
            IsAdd = false;
            try
            {
                var all = await _billingClient.ListTagsAsync();
                var t = all.FirstOrDefault(x => x.Id == EditId);
                if (t != null)
                    Form = new TagFormInput
                    {
                        Id = t.Id,
                        DisplayName = t.DisplayName,
                        Color = t.Color
                    };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tag for edit form. Id={Id}", EditId);
            }
        }

        await LoadTags();
    }

    public async Task<IActionResult> OnPostSaveAsync(bool isNew)
    {
        ShowForm = true;
        IsAdd = isNew;

        if (!ModelState.IsValid)
        {
            await LoadTags();
            return Page();
        }
        try
        {
            if (isNew)
            {
                var existing = await _billingClient.ListTagsAsync();
                if (existing.Any(x => x.Id == Form.Id))
                {
                    ModelState.AddModelError("Form.Id", $"Tag '{Form.Id}' already exists.");
                    await LoadTags();
                    return Page();
                }
            }

            await _billingClient.UpsertTagAsync(new TagInfo(
                Form.Id.Trim(),
                Form.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(Form.Color) ? null : Form.Color.Trim()));
            
            TempData["SuccessMessage"] = isNew ? "Tag added." : "Tag updated.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tag. Id={Id} IsNew={IsNew}", Form.Id, isNew);
            ErrorMessage = $"Failed to save tag: {ex.Message}";
            await LoadTags();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return RedirectToPage();
        try
        {
            await _billingClient.DeleteTagAsync(id);
            TempData["SuccessMessage"] = $"Tag '{id}' deleted.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tag. Id={Id}", id);
            ErrorMessage = $"Failed to delete tag: {ex.Message}";
            await LoadTags();
            return Page();
        }
    }

    private async Task LoadTags()
    {
        try
        {
            Tags = (await _billingClient.ListTagsAsync())
                .OrderBy(t => t.DisplayName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tags list.");
            ErrorMessage ??= "Failed to load tags.";
        }
    }

    public class TagFormInput
    {
        [Required]
        [RegularExpression(@"^[a-zA-Z0-9_\-\./]+$", ErrorMessage = "ID must be alphanumeric with dashes, underscores, dots or slashes")]
        public string Id { get; set; } = "";

        [Required] public string DisplayName { get; set; } = "";

        [RegularExpression(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", ErrorMessage = "Color must be a valid hex code starting with #")]
        public string? Color { get; set; }
    }
}
