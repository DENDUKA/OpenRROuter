using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class CreditsModel(BillingProvider BillingProvider, ILogger<CreditsModel> logger) : PageModelBase
{
    private readonly BillingProvider _billingClient = BillingProvider;

    public BalanceInfo? Balance { get; set; }
    public IReadOnlyList<TopUpInfo> TopUps { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try { Balance = await _billingClient.GetBalanceAsync(User.GetUserId()); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load balance. UserId={UserId}", User.GetUserId());
            ErrorMessage = "Failed to load balance.";
        }

        try { TopUps = await _billingClient.ListTopUpsAsync(User.GetUserId()); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load top-ups. UserId={UserId}", User.GetUserId());
        }
    }
}
