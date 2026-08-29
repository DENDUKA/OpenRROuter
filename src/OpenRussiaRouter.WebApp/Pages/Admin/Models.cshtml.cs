using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Reasoning;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.Enrichment;
using OpenRussiaRouter.WebApp.Services.Markup;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsModel(BillingProvider billingProvider, ControlPlaneProvider controlPlaneProvider, CollectorProvider collectorProvider, IOpenRouterCatalogClient openRouterClient, ILogger<ModelsModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly ControlPlaneProvider _controlPlaneClient = controlPlaneProvider;
    private readonly CollectorProvider _collectorProvider = collectorProvider;
    private readonly IOpenRouterCatalogClient _openRouterClient = openRouterClient;
    private readonly ILogger<ModelsModel> _logger = logger;

    public IReadOnlyList<ModelInfo> Models { get; set; } = [];
    public IReadOnlyList<string> AvailableNodeModels { get; set; } = [];
    public IReadOnlyList<TagInfo> AvailableTags { get; set; } = [];
    public Dictionary<string, TagInfo> TagsById { get; set; } = [];
    public IReadOnlyList<ProviderInfo> AvailableProviders { get; set; } = [];
    public Dictionary<string, ProviderInfo> ProvidersById { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public IReadOnlyList<string> WarningMessages { get; set; } = [];
    public string? Action => Request.Query["action"];
    public string? EditModelId => Request.Query["modelId"];

    public bool ShowForm { get; set; }
    public bool IsAdd { get; set; }

    /// <summary>
    /// Loads exact reasoning capabilities from the current 9Router catalog into the edit form.
    /// This is deliberately not a persistence operation: the administrator reviews the changes
    /// together with the rest of the form and commits them with Save.
    /// </summary>
    public async Task<IActionResult> OnPostSyncReasoningAsync(bool isNew)
    {
        ShowForm = true;
        IsAdd = isNew;
        ModelState.Clear();

        var loaded = await LoadNodeReasoningAsync();
        if (!loaded)
        {
            WarningMessages = ["9Router did not report reasoning capabilities for this model. The form was not changed."];
            await LoadFormData();
            return Page();
        }

        Form.AllowedReasoningEfforts = AvailableReasoningEfforts.ToList();
        Form.SupportsReasoning = Form.AllowedReasoningEfforts.Count > 0;
        Form.DefaultReasoningEffort = PickDefaultEffort(Form.AllowedReasoningEfforts, Form.DefaultReasoningEffort);
        Form.ReasoningEffortsOverridden = false;
        SuccessMessage = "Reasoning capabilities loaded from 9Router. Review the form and click Save to persist.";
        await LoadFormData();
        return Page();
    }

    /// <summary>
    /// Reasoning efforts available for selection in the edit form. Derived from the 9Router
    /// node catalog (cached via CollectorService) and mapped into the canonical ORR vocabulary
    /// by <see cref="NodeReasoningMapper"/>. Falls back to the full canonical set when the node
    /// hasn't reported <c>thinkingOptions</c> (e.g. node offline on first edit) so the admin can
    /// still curate efforts manually. <c>NodeReasoningMapper</c> only emits canonical tokens
    /// already understood by BillingService, so this list is always safe to persist.
    /// </summary>
    public IReadOnlyList<string> AvailableReasoningEfforts { get; set; } = [];
    public IReadOnlyList<ComboReasoningMemberInfo> ComboReasoningMembers { get; set; } = [];
    public IReadOnlyList<string> ModelTypes { get; } = ["text", "audio"];
    public IReadOnlyList<string> ModalityTypes { get; } = ["text", "image", "audio", "video"];

    /// <summary>Set when per-model enrich could not auto-match — UI prompts for an explicit OpenRouter id.</summary>
    public bool EnrichNotFound { get; set; }

    /// <summary>The OpenRouter id the admin typed for an explicit per-model enrich (kept across redisplay).</summary>
    public string? EnrichOpenRouterId { get; set; }

    /// <summary>Whether per-model enrich writes OpenRouter prices into the form fields (checked by default).</summary>
    public bool EnrichFillPrices { get; set; } = true;

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
                    // Load reasoning capabilities from node catalog
                    string? thinkingFormat = null;
                    bool thinkingCanDisable = true;
                    List<string> thinkingOptions = [];
                    List<string> reasoningEfforts = [];
                    IReadOnlyList<ComboReasoningMemberInfo> comboReasoningMembers = [];

                    try
                    {
                        var nodeCatalog = await _collectorProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);
                        var nodeEntry = nodeCatalog.Entries
                            .FirstOrDefault(e => string.Equals(e.UpstreamId, m.UpstreamModelId, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(e.PublicId, m.UpstreamModelId, StringComparison.OrdinalIgnoreCase));
                        if (nodeEntry is not null)
                        {
                            thinkingFormat = nodeEntry.ThinkingFormat;
                            thinkingCanDisable = nodeEntry.ThinkingCanDisable;
                            thinkingOptions = nodeEntry.ThinkingOptions.ToList();
                            reasoningEfforts = nodeEntry.ReasoningEfforts.ToList();
                            comboReasoningMembers = nodeEntry.ComboReasoningMembers;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load node catalog for reasoning info. ModelId={ModelId}", EditModelId);
                    }

                    // Build the pickable effort list from the node-reported vocabulary
                    // (mapped to canonical tokens). If the node hasn't reported anything —
                    // keep the full canonical fallback already set on the property — so the
                    // admin can still curate efforts manually.
                    AvailableReasoningEfforts = ResolveNodeEfforts(reasoningEfforts);
                    ComboReasoningMembers = comboReasoningMembers;

                    Form = new ModelFormInput
                    {
                        Id = m.Id,
                        DisplayName = m.DisplayName,
                        RouterModelId = m.UpstreamModelId,
                        InputPrice = m.InputPrice,
                        OutputPrice = m.OutputPrice,
                        CachePrice = m.CachePrice,
                        Enabled = m.Enabled,
                        DescriptionRu = m.DescriptionRu,
                        DescriptionEn = m.DescriptionEn,
                        ContextLength = m.ContextLength,
                        Modalities = m.Modalities,
                        InputModalities = m.InputModalities,
                        OutputModalities = m.OutputModalities,
                        Provider = m.Provider,
                        ProviderId = m.ProviderId,
                        TagIds = m.TagIds?.ToList() ?? [],
                        SupportsReasoning = m.SupportsReasoning,
                        AllowedReasoningEfforts = m.AllowedReasoningEfforts?.ToList() ?? [],
                        DefaultReasoningEffort = m.DefaultReasoningEffort,
                        ModelType = m.ModelType,
                        ThinkingFormat = thinkingFormat,
                        ThinkingCanDisable = thinkingCanDisable,
                        ThinkingOptions = thinkingOptions,
                        ReasoningEffortsOverridden = m.ReasoningEffortsOverridden,
                        Priority = m.Priority
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

        await LoadNodeReasoningAsync();
        NormalizeAndValidateReasoning();

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

            await LoadProviders();

            var routerModelId = string.IsNullOrWhiteSpace(Form.RouterModelId)
                    ? Form.Id.Trim()
                    : Form.RouterModelId.Trim();

            var providerId = string.IsNullOrWhiteSpace(Form.ProviderId) ? null : Form.ProviderId.Trim();
            var resolvedProvider = providerId is null
                ? (string.IsNullOrWhiteSpace(Form.Provider) ? null : Form.Provider.Trim())
                : (ProvidersById.TryGetValue(providerId, out var prov) ? prov.DisplayName : providerId);

            var m = new ModelInfo(
                    Form.Id.Trim(),
                    Form.DisplayName.Trim(),
                    routerModelId,
                    orig?.PriceVersion ?? "v1",
                    Form.InputPrice,
                    Form.OutputPrice,
                    Form.CachePrice,
                    Form.Enabled,
                    DescriptionRu: string.IsNullOrWhiteSpace(Form.DescriptionRu) ? null : Form.DescriptionRu.Trim(),
                    DescriptionEn: string.IsNullOrWhiteSpace(Form.DescriptionEn) ? null : Form.DescriptionEn.Trim(),
                    ContextLength: Form.ContextLength is > 0 ? Form.ContextLength : null,
                    Modalities: string.IsNullOrWhiteSpace(Form.Modalities) ? null : Form.Modalities.Trim(),
                    InputModalities: string.IsNullOrWhiteSpace(Form.InputModalities) ? null : Form.InputModalities.Trim(),
                    OutputModalities: string.IsNullOrWhiteSpace(Form.OutputModalities) ? null : Form.OutputModalities.Trim(),
                    DescriptionSource: DetermineSource(orig, Form),
                    Provider: resolvedProvider,
                    TagIds: Form.TagIds,
                    SupportsReasoning: Form.SupportsReasoning,
                    AllowedReasoningEfforts: Form.AllowedReasoningEfforts.Count > 0 ? Form.AllowedReasoningEfforts : null,
                    DefaultReasoningEffort: Form.DefaultReasoningEffort,
                    ModelType: Form.ModelType,
                    Priority: Form.Priority,
                    ProviderId: providerId,
                    ReasoningEffortsOverridden: Form.ReasoningEffortsOverridden);

            await _billingClient.UpsertModelAsync(m);
            TempData["SuccessMessage"] = isNew ? "Model added." : "Model updated.";
            return RedirectToPage(new { action = "edit", modelId = m.Id });
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
            var nodeCatalog = await _collectorProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);

            var models = await _billingClient.ListModelsAsync();
            int enriched = 0;
            int reasoningEnriched = 0;
            var notFound = new List<string>();
            var modelsToUpdate = new List<ModelInfo>();

            // Build node catalog lookup by upstream model id
            var nodeCatalogByUpstream = nodeCatalog.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.UpstreamId))
                .GroupBy(e => e.UpstreamId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var model in models)
            {
                var match = catalog.Count > 0 ? _openRouterClient.FindMatch(catalog, model.UpstreamModelId) : null;
                if (match is null && catalog.Count > 0)
                {
                    notFound.Add(model.UpstreamModelId);
                }

                // Enrich from OpenRouter (description, modalities, etc.)
                ModelInfo enrichedModel;
                if (match is not null && model.DescriptionSource != "manual")
                {
                    enrichedModel = model with
                    {
                        DescriptionEn = match.Description,
                        ContextLength = match.ContextLength,
                        Modalities = match.Modalities,
                        InputModalities = match.InputModalities,
                        OutputModalities = match.OutputModalities,
                        Provider = string.IsNullOrWhiteSpace(model.Provider) ? match.Provider : model.Provider,
                        DescriptionSource = "openrouter"
                    };
                    enriched++;
                }
                else
                {
                    enrichedModel = model;
                }

                // Enrich reasoning from 9Router node catalog.
                // 9Router emits vocabularies like ["auto","on","off"] (claude-adaptive /
                // gemini-level) or ["auto","none","low","medium","high"] (effort formats).
                // Those raw tokens are not all canonical in BillingService, so we translate
                // them through NodeReasoningMapper before persisting — otherwise the
                // upsert below would throw and abort the entire enrich batch.
                if (nodeCatalogByUpstream.TryGetValue(model.UpstreamModelId, out var nodeEntry)
                    && !enrichedModel.ReasoningEffortsOverridden)
                {
                    var mappedEfforts = nodeEntry.ReasoningEfforts;
                    var supportsReasoning = mappedEfforts.Count > 0;
                    var currentEfforts = enrichedModel.AllowedReasoningEfforts ?? [];
                    var currentDefaultIsValid = supportsReasoning
                        && mappedEfforts.Contains(enrichedModel.DefaultReasoningEffort ?? string.Empty, StringComparer.Ordinal);
                    ModelInfo? reasoningPatched = enrichedModel.SupportsReasoning != supportsReasoning
                        || !currentEfforts.SequenceEqual(mappedEfforts, StringComparer.Ordinal)
                        || !currentDefaultIsValid
                        ? enrichedModel with
                        {
                            SupportsReasoning = supportsReasoning,
                            AllowedReasoningEfforts = supportsReasoning ? mappedEfforts : null,
                            DefaultReasoningEffort = supportsReasoning
                                ? (currentDefaultIsValid
                                    ? enrichedModel.DefaultReasoningEffort
                                    : NodeReasoningMapper.PickDefaultEffort(mappedEfforts))
                                : null
                        }
                        : null;

                    if (reasoningPatched is not null)
                    {
                        enrichedModel = reasoningPatched;
                        reasoningEnriched++;
                    }
                }

                if (enrichedModel != model)
                {
                    modelsToUpdate.Add(enrichedModel);
                }
            }

            if (modelsToUpdate.Count > 0)
            {
                var (upsertedCount, failedIds) = await _billingClient.UpsertModelsAsync(modelsToUpdate, HttpContext.RequestAborted);
                if (failedIds.Count > 0)
                    _logger.LogWarning("Enrich: {FailedCount} model(s) failed to upsert: {FailedIds}", failedIds.Count, string.Join(", ", failedIds));
            }

            var msg = $"Enriched {enriched} model(s) from OpenRouter.";
            if (reasoningEnriched > 0)
                msg += $" Reasoning enabled for {reasoningEnriched} model(s) from 9Router.";
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
    public async Task<IActionResult> OnPostEnrichModelAsync(bool isNew, string? openRouterId, bool fillPrices)
    {
        ShowForm = true;
        IsAdd = isNew;
        EnrichOpenRouterId = openRouterId?.Trim();
        EnrichFillPrices = fillPrices;

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

            Form.DescriptionEn = match.Description;
            Form.DescriptionRu = null;
            Form.ContextLength = match.ContextLength;
            Form.Modalities = match.Modalities;
            Form.InputModalities = match.InputModalities;
            Form.OutputModalities = match.OutputModalities;
            if (!string.IsNullOrWhiteSpace(match.Provider))
            {
                Form.Provider = match.Provider;
                await LoadProviders();
                Form.ProviderId = ResolveProviderId(match.Provider);
            }

            // Echo the matched id back into the input so the admin sees exactly what was used.
            EnrichOpenRouterId = match.MatchedId;

            // Prices from OpenRouter are the upstream *cost* per 1M tokens. They are always surfaced
            // in the message for reference; they are only written into the form fields when the admin
            // left "Fill price fields" checked (so a description refresh can't clobber margins).
            var pricesNote = "";
            if (match.InputPrice is not null || match.OutputPrice is not null || match.CachePrice is not null)
            {
                if (EnrichFillPrices)
                {
                    if (match.InputPrice is not null) Form.InputPrice = match.InputPrice.Value;
                    if (match.OutputPrice is not null) Form.OutputPrice = match.OutputPrice.Value;
                    if (match.CachePrice is not null) Form.CachePrice = match.CachePrice.Value;
                    pricesNote = $" OpenRouter cost (in/out/cache per 1M): ${match.InputPrice:N4} / ${match.OutputPrice:N4} / ${match.CachePrice:N4} — set your selling price before saving.";
                }
                else
                {
                    pricesNote = $" OpenRouter cost (in/out/cache per 1M): ${match.InputPrice:N4} / ${match.OutputPrice:N4} / ${match.CachePrice:N4} (price fields left unchanged).";
                }
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

    public async Task<IActionResult> OnGetOpenRouterPriceAsync(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return new JsonResult(new { success = false, error = "Model ID is empty" });

        try
        {
            var catalog = await _openRouterClient.LoadCatalogAsync();
            if (catalog.Count == 0)
                return new JsonResult(new { success = false, error = "Не удалось загрузить каталог OpenRouter (CollectorService недоступен и прямой запрос к OpenRouter не удался). Проверьте интернет-соединение и состояние CollectorService." });

            var match = _openRouterClient.FindMatch(catalog, modelId);
            if (match == null)
                return new JsonResult(new { success = false, error = $"Model '{modelId}' not found on OpenRouter" });

            return new JsonResult(new
            {
                success = true,
                inputPrice = match.InputPrice,
                outputPrice = match.OutputPrice,
                cachePrice = match.CachePrice
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch OpenRouter price for model {ModelId}", modelId);
            return new JsonResult(new { success = false, error = "Internal error checking price" });
        }
    }

    private void NormalizeAndValidateReasoning()
    {
        Form.AllowedReasoningEfforts = Form.AllowedReasoningEfforts
            .Where(static effort => !string.IsNullOrWhiteSpace(effort))
            .Select(static effort => effort.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Form.DefaultReasoningEffort = string.IsNullOrWhiteSpace(Form.DefaultReasoningEffort)
            ? null
            : Form.DefaultReasoningEffort.Trim().ToLowerInvariant();

        foreach (var effort in Form.AllowedReasoningEfforts)
        {
            if (!AvailableReasoningEfforts.Contains(effort, StringComparer.Ordinal))
            {
                ModelState.AddModelError(nameof(Form.AllowedReasoningEfforts), $"Reasoning effort '{effort}' is not supported by 9Router for this model.");
            }
        }

        Form.SupportsReasoning = Form.AllowedReasoningEfforts.Count > 0;
        if (!Form.SupportsReasoning)
        {
            Form.DefaultReasoningEffort = null;
            Form.ReasoningEffortsOverridden = false;
            return;
        }

        if (Form.DefaultReasoningEffort is null || !Form.AllowedReasoningEfforts.Contains(Form.DefaultReasoningEffort, StringComparer.Ordinal))
        {
            Form.DefaultReasoningEffort = PickDefaultEffort(Form.AllowedReasoningEfforts, null);
        }

        Form.ReasoningEffortsOverridden = !Form.AllowedReasoningEfforts.SequenceEqual(AvailableReasoningEfforts, StringComparer.Ordinal);
    }

    private async Task<bool> LoadNodeReasoningAsync()
    {
        try
        {
            var catalog = await _collectorProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(catalog.Error))
                return false;

            var routerModelId = string.IsNullOrWhiteSpace(Form.RouterModelId) ? Form.Id : Form.RouterModelId;
            var nodeEntry = catalog.Entries.FirstOrDefault(entry =>
                string.Equals(entry.UpstreamId, routerModelId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.PublicId, routerModelId, StringComparison.OrdinalIgnoreCase));
            if (nodeEntry is null)
                return false;

            Form.ThinkingFormat = nodeEntry.ThinkingFormat;
            Form.ThinkingCanDisable = nodeEntry.ThinkingCanDisable;
            Form.ThinkingOptions = nodeEntry.ThinkingOptions.ToList();
            AvailableReasoningEfforts = ResolveNodeEfforts(nodeEntry.ReasoningEfforts);
            ComboReasoningMembers = nodeEntry.ComboReasoningMembers;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load 9Router reasoning capabilities. RouterModelId={RouterModelId}", Form.RouterModelId);
            return false;
        }
    }

    private static IReadOnlyList<string> ResolveNodeEfforts(IReadOnlyList<string> reasoningEfforts) =>
        reasoningEfforts
            .Where(static effort => NodeReasoningMapper.OrderedCanonical.Contains(effort, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string? PickDefaultEffort(IReadOnlyList<string> efforts, string? current)
    {
        if (!string.IsNullOrWhiteSpace(current) && efforts.Contains(current, StringComparer.Ordinal))
            return current;

        string[] preference = ["medium", "high", "low", "minimal", "xhigh", "max", "ultra", "none"];
        return preference.FirstOrDefault(efforts.Contains);
    }

    private static string? DetermineSource(ModelInfo? orig, ModelFormInput form)
    {
        var descRuChanged = (form.DescriptionRu?.Trim() ?? string.Empty) != (orig?.DescriptionRu ?? string.Empty);
        var descEnChanged = (form.DescriptionEn?.Trim() ?? string.Empty) != (orig?.DescriptionEn ?? string.Empty);
        var contextChanged = (form.ContextLength ?? 0) != (orig?.ContextLength ?? 0);
        var modalChanged = (form.Modalities?.Trim() ?? string.Empty) != (orig?.Modalities ?? string.Empty);
        var inputModalChanged = (form.InputModalities?.Trim() ?? string.Empty) != (orig?.InputModalities ?? string.Empty);
        var outputModalChanged = (form.OutputModalities?.Trim() ?? string.Empty) != (orig?.OutputModalities ?? string.Empty);

        return descRuChanged || descEnChanged || contextChanged || modalChanged || inputModalChanged || outputModalChanged
            ? "manual"
            : orig?.DescriptionSource;
    }

    private string? ResolveProviderId(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;
        return ProvidersById.Values
            .FirstOrDefault(p => string.Equals(p.DisplayName, providerName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private async Task LoadFormData()
    {
        await Task.WhenAll(LoadModels(), LoadAvailableNodeModels(), LoadTags(), LoadProviders());
    }

    private async Task LoadModels()
    {
        try
        {
            Models = (await _billingClient.ListModelsAsync())
                .OrderByDescending(model => model.Priority)
                .ThenByDescending(model => model.Enabled)
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
        AvailableNodeModels = await _collectorProvider.ListAvailableNodeModelsAsync();
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

    private async Task LoadProviders()
    {
        try
        {
            AvailableProviders = await _billingClient.ListModelProvidersAsync();
            ProvidersById = AvailableProviders.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load providers.");
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

        public string? DescriptionRu { get; set; }
        public string? DescriptionEn { get; set; }
        public int? ContextLength { get; set; }
        public string? Modalities { get; set; }
        public string? InputModalities { get; set; }
        public string? OutputModalities { get; set; }
        public string? Provider { get; set; }
        public string? ProviderId { get; set; }
        public List<string> TagIds { get; set; } = [];
        public bool SupportsReasoning { get; set; }
        public List<string> AllowedReasoningEfforts { get; set; } = [];
        public string? DefaultReasoningEffort { get; set; }
        public bool ReasoningEffortsOverridden { get; set; }
        public string ModelType { get; set; } = "text";
        public string? ThinkingFormat { get; set; }
        public bool ThinkingCanDisable { get; set; } = true;
        public List<string> ThinkingOptions { get; set; } = [];

        [Range(0, 100000)]
        public int Priority { get; set; }
    }

    public static string ModalityIcon(string modality) => ModalityIcons.GetIcon(modality, 14);
}
