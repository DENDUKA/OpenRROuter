using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsModel : PageModelBase
{
    private readonly BillingClient _billingClient;
    private readonly ControlPlaneClient _controlPlaneClient;

    public IReadOnlyList<ModelInfo> Models { get; set; } = [];
    public IReadOnlyList<string> AvailableNodeModels { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? EditModelId => Request.Query["modelId"];

    [BindProperty] public ModelFormInput Form { get; set; } = new();

    public ModelsModel(BillingClient billingClient, ControlPlaneClient controlPlaneClient)
    {
        _billingClient = billingClient;
        _controlPlaneClient = controlPlaneClient;
    }

    public async Task OnGetAsync()
    {
        if (Action == "edit" && EditModelId != null)
        {
            try
            {
                var all = await _billingClient.ListModelsAsync();
                var m = all.FirstOrDefault(x => x.Id == EditModelId);
                if (m != null)
                    Form = new ModelFormInput
                    {
                        Id = m.Id,
                        DisplayName = m.DisplayName,
                        RouterModelId = m.UpstreamModelId,
                        InputPrice = m.InputPrice,
                        OutputPrice = m.OutputPrice
                    };
            }
            catch { /* ignore */ }
        }

        await Task.WhenAll(LoadModels(), LoadAvailableNodeModels());
    }

    public async Task<IActionResult> OnPostSaveAsync(bool isNew)
    {
        if (!ModelState.IsValid)
        {
            await Task.WhenAll(LoadModels(), LoadAvailableNodeModels());
            return Page();
        }
        try
        {
            IReadOnlyList<ModelInfo> existing = [];
            if (!isNew)
                existing = await _billingClient.ListModelsAsync();

            var orig = isNew ? null : existing.FirstOrDefault(x => x.Id == Form.Id);
            var routerModelId = string.IsNullOrWhiteSpace(Form.RouterModelId)
                ? Form.Id.Trim()
                : Form.RouterModelId.Trim();

            var m = new ModelInfo(
                Form.Id.Trim(),
                Form.DisplayName.Trim(),
                routerModelId,
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
            await Task.WhenAll(LoadModels(), LoadAvailableNodeModels());
            return Page();
        }
    }

    private async Task LoadModels()
    {
        try { Models = await _billingClient.ListModelsAsync(); }
        catch { ErrorMessage ??= "Failed to load models."; }
    }

    private async Task LoadAvailableNodeModels()
    {
        AvailableNodeModels = await _controlPlaneClient.ListAvailableNodeModelsAsync();
    }

    public class ModelFormInput
    {
        [Required]
        [RegularExpression(@"^[a-zA-Z0-9_\-\./]+$", ErrorMessage = "ID must be alphanumeric with dashes, underscores, dots or slashes")]
        public string Id { get; set; } = "";

        [Required] public string DisplayName { get; set; } = "";

        public string RouterModelId { get; set; } = "";

        [Range(0, 1000000)] public decimal InputPrice { get; set; }

        [Range(0, 1000000)] public decimal OutputPrice { get; set; }
    }
}
