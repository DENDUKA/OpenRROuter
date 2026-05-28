using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AnomaliesModel(BillingProvider billingProvider, ILogger<AnomaliesModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingClient = billingProvider;

    public IReadOnlyList<AnomalyInfo> Anomalies { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try { Anomalies = await _billingClient.ListAnomaliesAsync(); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load anomalies.");
            ErrorMessage = "Failed to load anomalies.";
        }
    }
}
