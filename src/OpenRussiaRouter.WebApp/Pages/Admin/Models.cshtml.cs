using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsModel : PageModelBase
{
    private readonly BillingClient _billingClient;

    public IReadOnlyList<ModelInfo> Models { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? EditModelId => Request.Query["modelId"];

    [BindProperty] public ModelFormInput Form { get; set; } = new();

    public ModelsModel(BillingClient billingClient) => _billingClient = billingClient;

    public async Task OnGetAsync()
    {
        if (Action == "edit" && EditModelId != null)
        {
            try
            {
                var all = await _billingClient.ListModelsAsync();
                var m = all.FirstOrDefault(x => x.Id == EditModelId);
                if (m != null)
                    Form = new ModelFormInput { Id = m.Id, DisplayName = m.DisplayName, InputPrice = m.InputPrice, OutputPrice = m.OutputPrice };
            }
            catch { /* ignore */ }
        }
        await LoadModels();
    }

    public async Task<IActionResult> OnPostSaveAsync(bool isNew)
    {
        if (!ModelState.IsValid)
        {
            await LoadModels();
            return Page();
        }
        try
        {
            IReadOnlyList<ModelInfo> existing = [];
            if (!isNew)
                existing = await _billingClient.ListModelsAsync();

            var orig = isNew ? null : existing.FirstOrDefault(x => x.Id == Form.Id);
            var m = new ModelInfo(
                Form.Id.Trim(),
                Form.DisplayName.Trim(),
                orig?.UpstreamModelId ?? Form.Id.Trim(),
                orig?.PriceVersion ?? "v1",
                Form.InputPrice,
                Form.OutputPrice,
                orig?.CachePrice ?? 0m,
                orig?.Enabled ?? true);

            await _billingClient.UpsertModelAsync(m);
            TempData["SuccessMessage"] = isNew ? "Model added." : "Model updated.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save model: {ex.Message}";
            await LoadModels();
            return Page();
        }
    }

    private async Task LoadModels()
    {
        try { Models = await _billingClient.ListModelsAsync(); }
        catch { ErrorMessage ??= "Failed to load models."; }
    }

    public class ModelFormInput
    {
        [Required]
        [RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "ID must be alphanumeric with dashes/underscores")]
        public string Id { get; set; } = "";

        [Required] public string DisplayName { get; set; } = "";

        [Range(0, 1000000)] public decimal InputPrice { get; set; }

        [Range(0, 1000000)] public decimal OutputPrice { get; set; }
    }
}
