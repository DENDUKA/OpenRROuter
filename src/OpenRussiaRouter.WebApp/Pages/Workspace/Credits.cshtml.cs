using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class CreditsModel : PageModelBase
{
    private readonly BillingClient _billingClient;

    public BalanceInfo? Balance { get; set; }
    public string? ErrorMessage { get; set; }

    public CreditsModel(BillingClient billingClient) => _billingClient = billingClient;

    public async Task OnGetAsync()
    {
        try { Balance = await _billingClient.GetBalanceAsync(User.GetUserId()); }
        catch { ErrorMessage = "Failed to load balance."; }
    }
}
