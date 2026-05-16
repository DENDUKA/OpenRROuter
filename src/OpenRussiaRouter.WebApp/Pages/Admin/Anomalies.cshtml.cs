using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AnomaliesModel : PageModelBase
{
    private readonly BillingClient _billingClient;

    public IReadOnlyList<AnomalyInfo> Anomalies { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public AnomaliesModel(BillingClient billingClient) => _billingClient = billingClient;

    public async Task OnGetAsync()
    {
        try { Anomalies = await _billingClient.ListAnomaliesAsync(); }
        catch { ErrorMessage = "Failed to load anomalies."; }
    }
}
