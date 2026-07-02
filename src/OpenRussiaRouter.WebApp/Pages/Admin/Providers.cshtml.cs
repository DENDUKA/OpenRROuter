using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ProvidersModel(BillingProvider billingProvider, ILogger<ProvidersModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly ILogger<ProvidersModel> _logger = logger;

    public IReadOnlyList<ProviderInfo> Providers { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? EditId => Request.Query["id"];

    public bool ShowForm { get; set; }
    public bool IsAdd { get; set; }
    public string? ExistingLogoUrl { get; set; }

    [BindProperty] public ProviderFormInput Form { get; set; } = new();

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
                var provider = await _billingClient.GetModelProviderAsync(EditId);
                if (provider != null)
                {
                    Form = new ProviderFormInput
                    {
                        Id = provider.Id,
                        DisplayName = provider.DisplayName,
                        DescriptionRu = provider.DescriptionRu,
                        DescriptionEn = provider.DescriptionEn
                    };
                    ExistingLogoUrl = $"/admin/providers/logo/{Uri.EscapeDataString(provider.Id)}?v={provider.UpdatedAtUtc.UtcDateTime.Ticks}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load provider for edit form. Id={Id}", EditId);
            }
        }

        await LoadProviders();
    }

    public async Task<IActionResult> OnPostSaveAsync(bool isNew)
    {
        ShowForm = true;
        IsAdd = isNew;

        if (!ModelState.IsValid)
        {
            await LoadProviders();
            return Page();
        }

        try
        {
            if (isNew)
            {
                var existing = await _billingClient.ListModelProvidersAsync();
                if (existing.Any(x => x.Id == Form.Id))
                {
                    ModelState.AddModelError("Form.Id", $"Provider '{Form.Id}' already exists.");
                    await LoadProviders();
                    return Page();
                }
            }

            byte[]? logoBytes = null;
            string? logoContentType = null;
            bool clearLogo = false;

            if (Request.Form.ContainsKey("clearLogo"))
            {
                clearLogo = true;
            }

            var logoFile = Request.Form.Files.GetFile("logoFile");
            if (logoFile is { Length: > 0 })
            {
                var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp" };
                if (!allowedTypes.Contains(logoFile.ContentType))
                {
                    ModelState.AddModelError("logoFile", "Logo must be PNG, JPEG, or WebP.");
                    await LoadProviders();
                    return Page();
                }
                if (logoFile.Length > 256 * 1024)
                {
                    ModelState.AddModelError("logoFile", "Logo must be at most 256 KiB.");
                    await LoadProviders();
                    return Page();
                }
                using var ms = new MemoryStream();
                await logoFile.CopyToAsync(ms);
                logoBytes = ms.ToArray();
                logoContentType = logoFile.ContentType;
            }

            var existingProvider = isNew ? null : await _billingClient.GetModelProviderAsync(Form.Id);

            var info = new ProviderInfo(
                Form.Id.Trim(),
                Form.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(Form.DescriptionRu) ? null : Form.DescriptionRu.Trim(),
                string.IsNullOrWhiteSpace(Form.DescriptionEn) ? null : Form.DescriptionEn.Trim(),
                clearLogo ? null : (logoBytes ?? existingProvider?.LogoBytes),
                clearLogo ? null : (logoContentType ?? existingProvider?.LogoContentType));

            await _billingClient.UpsertModelProviderAsync(info);

            TempData["SuccessMessage"] = isNew ? "Provider added." : "Provider updated.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save provider. Id={Id} IsNew={IsNew}", Form.Id, isNew);
            ErrorMessage = $"Failed to save provider: {ex.Message}";
            await LoadProviders();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        try
        {
            await _billingClient.DeleteModelProviderAsync(id);
            TempData["SuccessMessage"] = "Provider deleted.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete provider. Id={Id}", id);
            TempData["ErrorMessage"] = $"Failed to delete provider: {ex.Message}";
        }
        return RedirectToPage();
    }

    private async Task LoadProviders()
    {
        try
        {
            Providers = (await _billingClient.ListModelProvidersAsync())
                .OrderBy(p => p.DisplayName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load providers list.");
            ErrorMessage ??= "Failed to load providers.";
        }
    }

    public class ProviderFormInput
    {
        [Required]
        [RegularExpression(@"^[a-zA-Z0-9_\-\./]+$", ErrorMessage = "ID must be alphanumeric with dashes, underscores, dots or slashes")]
        public string Id { get; set; } = "";

        [Required] public string DisplayName { get; set; } = "";

        public string? DescriptionRu { get; set; }

        public string? DescriptionEn { get; set; }
    }
}
