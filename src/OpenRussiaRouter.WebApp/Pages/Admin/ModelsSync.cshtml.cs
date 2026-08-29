using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Reasoning;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ModelsSyncModel(
    BillingProvider BillingProvider,
    ControlPlaneProvider ControlPlaneProvider,
    CollectorProvider CollectorProvider,
    RouterNodeApiProvider RouterNodeApiProvider,
    ILogger<ModelsSyncModel> logger) : PageModelBase
{
    public IReadOnlyList<NodeModelCatalogEntry> NewOnNodes { get; private set; } = [];
    public IReadOnlyList<ConfiguredModelRow> Configured { get; private set; } = [];
    public IReadOnlyList<ModelInfo> MissingFromNodes { get; private set; } = [];
    public IReadOnlyList<ModelInfo> BillingOnlyModels { get; private set; } = [];
    public IReadOnlyList<string> UnreachableNodeIds { get; private set; } = [];
    public IReadOnlyList<UnreachableNodeDetail> UnreachableNodes { get; private set; } = [];
    public IReadOnlyList<RowDisplay> RowDisplays { get; private set; } = [];
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
        BuildRowDisplays();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        Form.Prefix = Form.Prefix?.Trim() ?? string.Empty;
        var submittedRows = Form.Rows ?? [];
        var selectedCount = submittedRows.Count(r => r.Selected);
        logger.LogInformation(
            "Models sync import POST received. SubmittedRowCount={SubmittedRowCount}; SelectedCount={SelectedCount}",
            submittedRows.Count,
            selectedCount);

        var catalog = await CollectorProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);
        if (!string.IsNullOrWhiteSpace(catalog.Error))
        {
            ErrorMessage = $"Catalog unavailable — import cancelled: {catalog.Error}";
            await LoadDiffAsync(catalog);
            BuildRowDisplays();
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
            BuildRowDisplays();
            return Page();
        }

        BuildDiff(catalog, billingModels);

        if (selectedCount == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one model to import.");
            BuildRowDisplays();
            return Page();
        }

        if (!ModelState.IsValid)
        {
            BuildRowDisplays();
            return Page();
        }

        var imported = 0;
        var warnings = new List<string>();
        var byBillingId = billingModels.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var byRouterModel = billingModels
            .Where(m => !string.IsNullOrWhiteSpace(m.UpstreamModelId))
            .GroupBy(m => m.UpstreamModelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var catalogByUpstream = catalog.Entries
            .GroupBy(e => e.UpstreamId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var selectedRows = submittedRows
            .Where(r => r.Selected && !string.IsNullOrWhiteSpace(r.UpstreamId))
            .OrderBy(r => r.UpstreamId, StringComparer.Ordinal)
            .ToList();

        foreach (var row in selectedRows)
        {
            if (!catalogByUpstream.TryGetValue(row.UpstreamId, out var entry))
            {
                warnings.Add($"{row.UpstreamId}: model no longer on nodes.");
                continue;
            }

            var upstreamId = entry.UpstreamId;
            var routerModelId = GetRouterModelId(entry);
            var modelId = Form.Prefix + routerModelId;
            if (byRouterModel.TryGetValue(routerModelId, out var existingByRouterModel))
            {
                warnings.Add($"{routerModelId}: already configured under '{existingByRouterModel.Id}'.");
                continue;
            }

            if (byBillingId.ContainsKey(modelId))
            {
                warnings.Add($"{routerModelId}: billing ID '{modelId}' already used.");
                continue;
            }

            // The exact per-model list is supplied by 9Router. thinkingOptions is an
            // implementation/UI detail and must not be used as a public capability list.
            var mappedEfforts = entry.ReasoningEfforts;
            var supportsReasoning = mappedEfforts.Count > 0;

            var model = new ModelInfo(
                modelId,
                entry.DisplayName,
                routerModelId,
                "v1",
                row.InputPrice,
                row.OutputPrice,
                row.CachePrice,
                row.Enabled,
                ModelType: string.IsNullOrWhiteSpace(entry.ModelType) ? "text" : entry.ModelType,
                SupportsReasoning: supportsReasoning,
                AllowedReasoningEfforts: supportsReasoning ? mappedEfforts : null,
                DefaultReasoningEffort: supportsReasoning
                    ? NodeReasoningMapper.PickDefaultEffort(mappedEfforts)
                    : null,
                ReasoningEffortsOverridden: false);

            try
            {
                await BillingProvider.UpsertModelAsync(model, HttpContext.RequestAborted);
                byBillingId[model.Id] = model;
                byRouterModel[model.UpstreamModelId] = model;
                imported++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to import model {ModelId} from upstream {UpstreamId}", model.Id, model.UpstreamModelId);
                warnings.Add($"{routerModelId}: import failed: {ex.Message}");
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

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        probeCts.CancelAfter(TimeSpan.FromSeconds(5));

        var probeTasks = UnreachableNodeIds.Select(async nodeId =>
        {
            if (!byId.TryGetValue(nodeId, out var node))
            {
                return new UnreachableNodeDetail(nodeId, "(node not found in registry)", string.Empty);
            }

            try
            {
                var result = await RouterNodeApiProvider.ProbeProvidersAsync(node, probeCts.Token);
                return new UnreachableNodeDetail(nodeId, result.RequestLine, result.ResponseLine);
            }
            catch (OperationCanceledException)
            {
                return new UnreachableNodeDetail(nodeId, "(probe timed out)", string.Empty);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to probe node {NodeId}", nodeId);
                return new UnreachableNodeDetail(nodeId, "(probe failed)", string.Empty);
            }
        });

        UnreachableNodes = await Task.WhenAll(probeTasks);
    }

    private async Task LoadDiffAsync(NodeModelCatalogResult? preloadedCatalog = null)
    {
        NodeModelCatalogResult catalog;
        IReadOnlyList<ModelInfo> billingModels = [];

        if (preloadedCatalog is null)
        {
            catalog = await CollectorProvider.ListNodeModelCatalogAsync(HttpContext.RequestAborted);
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
        var byRouterModel = billingModels
            .Where(m => !string.IsNullOrWhiteSpace(m.UpstreamModelId))
            .GroupBy(m => m.UpstreamModelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var catalogRouterModelIds = catalog.Entries
            .Select(GetRouterModelId)
            .ToHashSet(StringComparer.Ordinal);

        NewOnNodes = catalog.Entries
            .Where(entry =>
            {
                var routerModelId = GetRouterModelId(entry);
                return !byRouterModel.ContainsKey(routerModelId) && !byBillingId.ContainsKey(routerModelId);
            })
            .OrderBy(GetRouterModelId, StringComparer.Ordinal)
            .ToList();

        Configured = catalog.Entries
            .Where(entry => byRouterModel.ContainsKey(GetRouterModelId(entry)))
            .Select(entry =>
            {
                var routerModelId = GetRouterModelId(entry);
                var billing = byRouterModel[routerModelId];
                return new ConfiguredModelRow(
                    billing.Id,
                    billing.DisplayName,
                    routerModelId,
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
                .Where(model => !catalogRouterModelIds.Contains(model.UpstreamModelId))
                .OrderBy(model => model.Id, StringComparer.Ordinal)
                .ToList();

        ShowAddNodeMessage = catalog.Entries.Count == 0 && !CatalogUnavailable && catalog.UnreachableNodeIds.Count == 0;
    }

    private void BuildRowDisplays()
    {
        var submitted = (Form.Rows ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.UpstreamId))
            .GroupBy(r => r.UpstreamId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rows = new List<ModelImportRow>(NewOnNodes.Count);
        var displays = new List<RowDisplay>(NewOnNodes.Count);

        for (var i = 0; i < NewOnNodes.Count; i++)
        {
            var entry = NewOnNodes[i];
            var row = submitted.TryGetValue(entry.UpstreamId, out var existing)
                ? existing
                : new ModelImportRow { UpstreamId = entry.UpstreamId, Enabled = true };

            row.UpstreamId = entry.UpstreamId;
            displays.Add(new RowDisplay(i, entry));
            rows.Add(row);
        }

        Form.Rows = rows;
        RowDisplays = displays;
    }

    public sealed class ImportFormInput
    {
        // Optional. Must be nullable: a non-nullable string under <Nullable>enable</Nullable>
        // gets an implicit [Required] that rejects an empty prefix and silently blocks import.
        public string? Prefix { get; set; }

        public List<ModelImportRow> Rows { get; set; } = [];
    }

    public sealed class ModelImportRow
    {
        public string UpstreamId { get; set; } = "";

        public bool Selected { get; set; }

        [Range(0, 1000000, ErrorMessage = "Input price must be between 0 and 1,000,000.")]
        public decimal InputPrice { get; set; }

        [Range(0, 1000000, ErrorMessage = "Output price must be between 0 and 1,000,000.")]
        public decimal OutputPrice { get; set; }

        [Range(0, 1000000, ErrorMessage = "Cache price must be between 0 and 1,000,000.")]
        public decimal CachePrice { get; set; }

        public bool Enabled { get; set; } = true;
    }

    public sealed record RowDisplay(int FormIndex, NodeModelCatalogEntry Entry);

    public sealed record ConfiguredModelRow(
        string BillingId,
        string BillingDisplayName,
        string UpstreamId,
        string ModelType,
        IReadOnlyList<string> SourceProviders,
        bool Enabled);

    public static string GetRouterModelId(NodeModelCatalogEntry entry) =>
        string.IsNullOrWhiteSpace(entry.PublicId)
            ? entry.UpstreamId
            : entry.PublicId;

}
