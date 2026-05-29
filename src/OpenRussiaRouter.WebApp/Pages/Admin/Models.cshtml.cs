using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.Enrichment;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsModel(BillingProvider billingProvider, ControlPlaneProvider controlPlaneProvider, IOpenRouterCatalogClient openRouterClient, ILogger<ModelsModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly ControlPlaneProvider _controlPlaneClient = controlPlaneProvider;
    private readonly IOpenRouterCatalogClient _openRouterClient = openRouterClient;
    private readonly ILogger<ModelsModel> _logger = logger;

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
                        Enabled = m.Enabled,
                        Description = m.Description,
                        ContextLength = m.ContextLength,
                        Modalities = m.Modalities
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
                Form.Enabled,
                Description: string.IsNullOrWhiteSpace(Form.Description) ? null : Form.Description.Trim(),
                ContextLength: Form.ContextLength is > 0 ? Form.ContextLength : null,
                Modalities: string.IsNullOrWhiteSpace(Form.Modalities) ? null : Form.Modalities.Trim(),
                DescriptionSource: DetermineSource(orig, Form));

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

    public async Task<IActionResult> OnPostEnrichAsync()
    {
        try
        {
            var catalog = await _openRouterClient.LoadCatalogAsync();
            if (catalog.Count == 0)
            {
                TempData["WarningMessages"] = "OpenRouter catalog is empty or unreachable.";
                return RedirectToPage();
            }

            var models = await _billingClient.ListModelsAsync();
            int enriched = 0;
            var notFound = new List<string>();

            foreach (var model in models)
            {
                var match = _openRouterClient.FindMatch(catalog, model.UpstreamModelId);
                if (match is null)
                {
                    notFound.Add(model.UpstreamModelId);
                    continue;
                }

                if (model.DescriptionSource == "manual")
                    continue;

                var enrichedModel = model with
                {
                    Description = match.Description,
                    ContextLength = match.ContextLength,
                    Modalities = match.Modalities,
                    DescriptionSource = "openrouter"
                };
                await _billingClient.UpsertModelAsync(enrichedModel);
                enriched++;
            }

            var msg = $"Enriched {enriched} model(s) from OpenRouter.";
            if (notFound.Count > 0)
                msg += $" Not found: {string.Join(", ", notFound)}.";

            TempData["SuccessMessage"] = msg;
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enrich from OpenRouter failed");
            TempData["WarningMessages"] = $"Enrichment failed: {ex.Message}";
            return RedirectToPage();
        }
    }

    private static string? DetermineSource(ModelInfo? orig, ModelFormInput form)
    {
        var descChanged = (form.Description?.Trim() ?? string.Empty) != (orig?.Description ?? string.Empty);
        var contextChanged = (form.ContextLength ?? 0) != (orig?.ContextLength ?? 0);
        var modalChanged = (form.Modalities?.Trim() ?? string.Empty) != (orig?.Modalities ?? string.Empty);

        return descChanged || contextChanged || modalChanged
            ? "manual"
            : orig?.DescriptionSource;
    }

    private async Task LoadFormData()
    {
        await Task.WhenAll(LoadModels(), LoadAvailableNodeModels());
    }

    private async Task LoadModels()
    {
        try
        {
            Models = (await _billingClient.ListModelsAsync())
                .OrderByDescending(model => model.Enabled)
                .ThenBy(model => model.Id, StringComparer.Ordinal)
                .ToList();
        }
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

        public string? Description { get; set; }
        public int? ContextLength { get; set; }
        public string? Modalities { get; set; }
    }
}
