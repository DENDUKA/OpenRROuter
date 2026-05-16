using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class ActivityModel : PageModelBase
{
    private readonly BillingClient _billingClient;

    public IReadOnlyList<UsageInfo> Usage { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public ActivityModel(BillingClient billingClient) => _billingClient = billingClient;

    public async Task OnGetAsync()
    {
        try { Usage = await _billingClient.ListUsageAsync(User.GetUserId()); }
        catch { ErrorMessage = "Failed to load usage data."; }
    }
}
