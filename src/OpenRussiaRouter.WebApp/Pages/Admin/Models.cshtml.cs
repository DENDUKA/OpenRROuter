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
    public IReadOnlyList<TagInfo> AvailableTags { get; set; } = [];
    public Dictionary<string, TagInfo> TagsById { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public IReadOnlyList<string> WarningMessages { get; set; } = [];
    public string? Action => Request.Query["action"];
    public string? EditModelId => Request.Query["modelId"];

    public bool ShowForm { get; set; }
    public bool IsAdd { get; set; }

    /// <summary>Set when per-model enrich could not auto-match — UI prompts for an explicit OpenRouter id.</summary>
    public bool EnrichNotFound { get; set; }

    /// <summary>The OpenRouter id the admin typed for an explicit per-model enrich (kept across redisplay).</summary>
    public string? EnrichOpenRouterId { get; set; }

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
                {
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
                        Modalities = m.Modalities,
                        Provider = m.Provider,
                        TagIds = m.TagIds?.ToList() ?? []
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load model for edit form. ModelId={ModelId}", EditModelId);
            }
        }

        if (ShowForm)
        {
            await LoadFormData();
        }
        else
        {
            await Task.WhenAll(LoadModels(), LoadTags());
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

            if (!isNew && orig == null)
            {
                ModelState.AddModelError("Form.Id", $"Model '{Form.Id}' not found.");
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
                DescriptionSource: DetermineSource(orig, Form),
                Provider: string.IsNullOrWhiteSpace(Form.Provider) ? null : Form.Provider.Trim(),
                TagIds: Form.TagIds);

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
                    Provider = string.IsNullOrWhiteSpace(model.Provider) ? match.Provider : model.Provider,
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

    /// <summary>
    /// Per-model enrich invoked from the edit/add form. Fills the form's metadata fields from
    /// OpenRouter so the admin can review and Save. Does not persist on its own.
    /// If <paramref name="openRouterId"/> is given it is matched explicitly; otherwise auto-match
    /// runs against the Router Model / Model ID, and on failure the UI asks for an explicit id.
    /// </summary>
    public async Task<IActionResult> OnPostEnrichModelAsync(bool isNew, string? openRouterId)
    {
        ShowForm = true;
        IsAdd = isNew;
        EnrichOpenRouterId = openRouterId?.Trim();

        // This is a "fill the form" action, not a save — drop any premature validation errors
        // (e.g. a still-empty Display Name while adding) so the redisplayed form stays clean.
        ModelState.Clear();

        try
        {
            var catalog = await _openRouterClient.LoadCatalogAsync();
            if (catalog.Count == 0)
            {
                WarningMessages = ["OpenRouter catalog is empty or unreachable."];
                await LoadFormData();
                return Page();
            }

            var lookup = !string.IsNullOrWhiteSpace(EnrichOpenRouterId)
                ? EnrichOpenRouterId
                : (string.IsNullOrWhiteSpace(Form.RouterModelId) ? Form.Id : Form.RouterModelId);

            var match = _openRouterClient.FindMatch(catalog, lookup ?? string.Empty);
            if (match is null)
            {
                EnrichNotFound = true;
                WarningMessages = string.IsNullOrWhiteSpace(EnrichOpenRouterId)
                    ? [$"Could not auto-match '{lookup}' on OpenRouter. Enter the OpenRouter model id below and try again."]
                    : [$"OpenRouter has no model '{EnrichOpenRouterId}'. Check the id and try again."];
                await LoadFormData();
                return Page();
            }

            Form.Description = match.Description;
            Form.ContextLength = match.ContextLength;
            Form.Modalities = match.Modalities;
            if (!string.IsNullOrWhiteSpace(match.Provider))
                Form.Provider = match.Provider;

            // Echo the matched id back into the input so the admin sees exactly what was used.
            EnrichOpenRouterId = match.MatchedId;

            // Prices from OpenRouter are the upstream *cost* per 1M tokens — fill them so the admin
            // has a reference, but they must set their own selling price (margin) before saving.
            var pricesNote = "";
            if (match.InputPrice is not null || match.OutputPrice is not null || match.CachePrice is not null)
            {
                if (match.InputPrice is not null) Form.InputPrice = match.InputPrice.Value;
                if (match.OutputPrice is not null) Form.OutputPrice = match.OutputPrice.Value;
                if (match.CachePrice is not null) Form.CachePrice = match.CachePrice.Value;
                pricesNote = $" OpenRouter cost (in/out/cache per 1M): ${Form.InputPrice:N4} / ${Form.OutputPrice:N4} / ${Form.CachePrice:N4} — set your selling price before saving.";
            }

            SuccessMessage = $"Filled from OpenRouter ({match.MatchedId}). Review the fields and click Save to persist.{pricesNote}";
            await LoadFormData();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Per-model enrich from OpenRouter failed. ModelId={ModelId}", Form.Id);
            WarningMessages = [$"Enrichment failed: {ex.Message}"];
            await LoadFormData();
            return Page();
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
        await Task.WhenAll(LoadModels(), LoadAvailableNodeModels(), LoadTags());
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

    private async Task LoadTags()
    {
        try
        {
            AvailableTags = await _billingClient.ListTagsAsync();
            TagsById = AvailableTags.ToDictionary(t => t.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tags.");
        }
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
        public string? Provider { get; set; }
        public List<string> TagIds { get; set; } = [];
    }
}
