using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsModel : PageModelBase
{
    private readonly BillingProvider _billingClient;
    private readonly ControlPlaneProvider _controlPlaneClient;
    private readonly ILogger<ModelsModel> _logger;

    public IReadOnlyList<ModelInfo> Models { get; set; } = [];
    public IReadOnlyList<string> AvailableNodeModels { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public IReadOnlyList<string> WarningMessages { get; set; } = [];
    public string? Action => Request.Query["action"];
    public string? EditModelId => Request.Query["modelId"];

    public bool ShowForm { get; set; }
    public bool IsAdd { get; set; }

    [BindProperty] public ModelFormInput Form { get; set; } = new();

    public ModelsModel(BillingProvider BillingProvider, ControlPlaneProvider ControlPlaneProvider, ILogger<ModelsModel> logger)
    {
        _billingClient = BillingProvider;
        _controlPlaneClient = ControlPlaneProvider;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string success)
            SuccessMessage = success;
        if (TempData["WarningMessages"] is string warnings)
            WarningMessages = warnings.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        if (Action == "add")
        {
            ShowForm = true;
            IsAdd = true;
        }
        else if (Action == "edit" && EditModelId != null)
        {
            ShowForm = true;
            IsAdd = false;
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
                        OutputPrice = m.OutputPrice,
                        CachePrice = m.CachePrice,
                        Enabled = m.Enabled
                    };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load model for edit form. ModelId={ModelId}", EditModelId);
            }
        }

        if (ShowForm)
        {
            await Task.WhenAll(LoadModels(), LoadAvailableNodeModels());
        }
        else
        {
            await LoadModels();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(bool isNew)
    {
        ShowForm = true;
        IsAdd = isNew;

        if (!ModelState.IsValid)
        {
            await LoadFormData();
            return Page();
        }
        try
        {
            IReadOnlyList<ModelInfo> existing = await _billingClient.ListModelsAsync();
            var orig = existing.FirstOrDefault(x => x.Id == Form.Id);

            if (isNew && orig != null)
            {
                ModelState.AddModelError("Form.Id", $"Model '{Form.Id}' already exists.");
                await LoadFormData();
                return Page();
            }

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
                Form.CachePrice,
                Form.Enabled);

            await _billingClient.UpsertModelAsync(m);
            TempData["SuccessMessage"] = isNew ? "Model added." : "Model updated.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save model. ModelId={ModelId} IsNew={IsNew}", Form.Id, isNew);
            ErrorMessage = $"Failed to save model: {ex.Message}";
            await LoadFormData();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return RedirectToPage();
        try
        {
            var existing = await _billingClient.ListModelsAsync();
            var m = existing.FirstOrDefault(x => x.Id == modelId);
            if (m == null)
            {
                TempData["SuccessMessage"] = null;
                ErrorMessage = $"Model '{modelId}' not found.";
                await LoadModels();
                return Page();
            }
            await _billingClient.UpsertModelAsync(m with { Enabled = !m.Enabled });
            TempData["SuccessMessage"] = m.Enabled ? $"Model '{modelId}' disabled." : $"Model '{modelId}' enabled.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle model enabled. ModelId={ModelId}", modelId);
            ErrorMessage = $"Failed to toggle model: {ex.Message}";
            await LoadModels();
            return Page();
        }
    }

    private async Task LoadFormData()
    {
        await Task.WhenAll(LoadModels(), LoadAvailableNodeModels());
    }

    private async Task LoadModels()
    {
        try { Models = await _billingClient.ListModelsAsync(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load models list.");
            ErrorMessage ??= "Failed to load models.";
        }
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

        [Range(0, 1000000)] public decimal CachePrice { get; set; }

        public bool Enabled { get; set; } = true;
    }
}
