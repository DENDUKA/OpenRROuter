using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsSyncModel(
    BillingProvider BillingProvider,
    ControlPlaneProvider ControlPlaneProvider,
    RouterNodeApiProvider RouterNodeApiProvider,
    ILogger<ModelsSyncModel> logger) : PageModelBase
{
    public IReadOnlyList<NodeModelCatalogEntry> NewOnNodes { get; private set; } = [];
    public IReadOnlyList<ConfiguredModelRow> Configured { get; private set; } = [];
    public IReadOnlyList<ModelInfo> MissingFromNodes { get; private set; } = [];
    public IReadOnlyList<ModelInfo> BillingOnlyModels { get; private set; } = [];
    public IReadOnlyList<string> UnreachableNodeIds { get; private set; } = [];
    public IReadOnlyList<UnreachableNodeDetail> UnreachableNodes { get; private set; } = [];
    public HashSet<string> SelectedUpstreamIds { get; private set; } = new(StringComparer.Ordinal);
    public List<string> WarningMessages { get; } = [];
    public string? ErrorMessage { get; private set; }
    public bool CatalogUnavailable { get; private set; }
    public bool BillingUnavailable { get; private set; }
    public bool MissingFromNodesSuppressed { get; private set; }
    public bool ShowAddNodeMessage { get; private set; }

    [BindProperty] public ImportFormInput Form { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadDiffAsync();
        await ProbeUnreachableNodesAsync();
    }

    public async Task<IActionResult> OnPostImportAsync([FromForm(Name = "selectedUpstreamIds")] string[] selectedUpstreamIds)
    {
        SelectedUpstreamIds = selectedUpstreamIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        Form.Prefix = Form.Prefix?.Trim() ?? string.Empty;
        logger.LogInformation(
            "Models sync import POST received. SelectedCount={SelectedCount}; SelectedUpstreamIds={SelectedUpstreamIds}",
            SelectedUpstreamIds.Count,
            string.Join(", ", SelectedUpstreamIds.OrderBy(id => id, StringComparer.Ordinal)));

        var catalog = await ControlPlaneProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);
        if (!string.IsNullOrWhiteSpace(catalog.Error))
        {
            ErrorMessage = $"Catalog unavailable — import cancelled: {catalog.Error}";
            await LoadDiffAsync(catalog);
            return Page();
        }

        IReadOnlyList<ModelInfo> billingModels;
        try
        {
            billingModels = await BillingProvider.ListModelsAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load billing catalog during model sync import");
            ErrorMessage = "Billing catalog unavailable — import cancelled.";
            BillingUnavailable = true;
            BuildDiff(catalog, []);
            return Page();
        }

        BuildDiff(catalog, billingModels);

        if (SelectedUpstreamIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one model to import.");
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var imported = 0;
        var warnings = new List<string>();
        var byBillingId = billingModels.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var byUpstream = billingModels
            .Where(m => !string.IsNullOrWhiteSpace(m.UpstreamModelId))
            .GroupBy(m => m.UpstreamModelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var catalogByUpstream = catalog.Entries
            .GroupBy(e => e.UpstreamId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var upstreamId in SelectedUpstreamIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!catalogByUpstream.TryGetValue(upstreamId, out var entry))
            {
                warnings.Add($"{upstreamId}: model no longer on nodes.");
                continue;
            }

            var modelId = Form.Prefix + entry.PublicId;
            if (byUpstream.TryGetValue(entry.UpstreamId, out var existingByUpstream))
            {
                warnings.Add($"{entry.UpstreamId}: already configured under '{existingByUpstream.Id}'.");
                continue;
            }

            if (byBillingId.ContainsKey(modelId))
            {
                warnings.Add($"{entry.UpstreamId}: billing ID '{modelId}' already used.");
                continue;
            }

            var model = new ModelInfo(
                modelId,
                entry.DisplayName,
                entry.UpstreamId,
                "v1",
                Form.InputPrice,
                Form.OutputPrice,
                Form.CachePrice,
                Form.EnableOnImport);

            try
            {
                await BillingProvider.UpsertModelAsync(model, HttpContext.RequestAborted);
                byBillingId[model.Id] = model;
                byUpstream[model.UpstreamModelId] = model;
                imported++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to import model {ModelId} from upstream {UpstreamId}", model.Id, model.UpstreamModelId);
                warnings.Add($"{entry.UpstreamId}: import failed: {ex.Message}");
            }
        }

        TempData["SuccessMessage"] = $"Imported {imported} models.";
        if (warnings.Count > 0)
        {
            TempData["WarningMessages"] = string.Join(Environment.NewLine, warnings);
        }

        logger.LogInformation(
            "Models sync import completed. ImportedCount={ImportedCount}; WarningCount={WarningCount}",
            imported,
            warnings.Count);

        return RedirectToPage("/Admin/Models");
    }

    private async Task ProbeUnreachableNodesAsync()
    {
        if (UnreachableNodeIds.Count == 0)
            return;

        IReadOnlyList<RouterNodeInfo> nodes;
        try
        {
            nodes = await ControlPlaneProvider.ListRouterNodesAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list nodes for unreachable probe");
            return;
        }

        var byId = nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        var probed = new List<UnreachableNodeDetail>();

        foreach (var nodeId in UnreachableNodeIds)
        {
            if (!byId.TryGetValue(nodeId, out var node))
            {
                probed.Add(new UnreachableNodeDetail(nodeId, "(node not found in registry)", string.Empty));
                continue;
            }

            var result = await RouterNodeApiProvider.ProbeProvidersAsync(node, HttpContext.RequestAborted);
            probed.Add(new UnreachableNodeDetail(nodeId, result.RequestLine, result.ResponseLine));
        }

        UnreachableNodes = probed;
    }

    private async Task LoadDiffAsync(NodeModelCatalogResult? preloadedCatalog = null)
    {
        NodeModelCatalogResult catalog;
        IReadOnlyList<ModelInfo> billingModels = [];

        if (preloadedCatalog is null)
        {
            catalog = await ControlPlaneProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);
        }
        else
        {
            catalog = preloadedCatalog;
        }

        try
        {
            billingModels = await BillingProvider.ListModelsAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load billing catalog for model sync page");
            BillingUnavailable = true;
            ErrorMessage ??= "Billing catalog unavailable.";
        }

        BuildDiff(catalog, billingModels);
    }

    private void BuildDiff(NodeModelCatalogResult catalog, IReadOnlyList<ModelInfo> billingModels)
    {
        UnreachableNodeIds = catalog.UnreachableNodeIds;
        UnreachableNodes = catalog.UnreachableNodes;
        CatalogUnavailable = !string.IsNullOrWhiteSpace(catalog.Error);
        if (CatalogUnavailable)
        {
            ErrorMessage ??= "catalog unavailable";
            BillingOnlyModels = billingModels.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
            NewOnNodes = [];
            Configured = [];
            MissingFromNodes = [];
            MissingFromNodesSuppressed = false;
            return;
        }

        if (BillingUnavailable)
        {
            NewOnNodes = [];
            Configured = [];
            MissingFromNodes = [];
            MissingFromNodesSuppressed = false;
            return;
        }

        var byBillingId = billingModels.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var byUpstream = billingModels
            .Where(m => !string.IsNullOrWhiteSpace(m.UpstreamModelId))
            .GroupBy(m => m.UpstreamModelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var catalogUpstreamIds = catalog.Entries
            .Select(e => e.UpstreamId)
            .ToHashSet(StringComparer.Ordinal);

        NewOnNodes = catalog.Entries
            .Where(entry => !byUpstream.ContainsKey(entry.UpstreamId) && !byBillingId.ContainsKey(entry.PublicId))
            .OrderBy(entry => entry.PublicId, StringComparer.Ordinal)
            .ToList();

        Configured = catalog.Entries
            .Where(entry => byUpstream.ContainsKey(entry.UpstreamId))
            .Select(entry =>
            {
                var billing = byUpstream[entry.UpstreamId];
                return new ConfiguredModelRow(
                    billing.Id,
                    billing.DisplayName,
                    entry.UpstreamId,
                    entry.ModelType,
                    entry.SourceProviders,
                    billing.Enabled);
            })
            .OrderBy(row => row.BillingId, StringComparer.Ordinal)
            .ToList();

        MissingFromNodesSuppressed = catalog.UnreachableNodeIds.Count > 0;
        MissingFromNodes = MissingFromNodesSuppressed
            ? []
            : billingModels
                .Where(model => !catalogUpstreamIds.Contains(model.UpstreamModelId))
                .OrderBy(model => model.Id, StringComparer.Ordinal)
                .ToList();

        ShowAddNodeMessage = catalog.Entries.Count == 0 && !CatalogUnavailable && catalog.UnreachableNodeIds.Count == 0;
    }

    public sealed class ImportFormInput
    {
        [Range(0, 1000000)] public decimal InputPrice { get; set; }

        [Range(0, 1000000)] public decimal OutputPrice { get; set; }

        [Range(0, 1000000)] public decimal CachePrice { get; set; }

        public bool EnableOnImport { get; set; } = true;

        public string Prefix { get; set; } = "";
    }

    public sealed record ConfiguredModelRow(
        string BillingId,
        string BillingDisplayName,
        string UpstreamId,
        string ModelType,
        IReadOnlyList<string> SourceProviders,
        bool Enabled);
}
