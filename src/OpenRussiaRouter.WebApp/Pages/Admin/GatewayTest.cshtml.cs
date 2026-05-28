using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class GatewayTestModel(
    BillingProvider billingProvider,
    ILogger<GatewayTestModel> logger) : PageModelBase
{
    public IReadOnlyList<ModelOption> Models { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            Models = (await billingProvider.ListModelsAsync(HttpContext.RequestAborted))
                .OrderByDescending(m => m.Enabled)
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new ModelOption(
                    m.UpstreamModelId,
                    m.Enabled ? m.DisplayName : $"{m.DisplayName} (disabled)",
                    m.Enabled))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load models for gateway test page.");
            ErrorMessage = "Failed to load models.";
        }
    }

    public sealed record ModelOption(string Value, string Label, bool Enabled);
}
