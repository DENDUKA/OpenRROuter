using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class ActivityModel : PageModelBase
{
    private readonly BillingClient _billingClient;
    private readonly AuthClient _authClient;

    public IReadOnlyList<ActivityByTokenInfo> Activity { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public ActivityModel(BillingClient billingClient, AuthClient authClient)
    {
        _billingClient = billingClient;
        _authClient = authClient;
    }

    public async Task OnGetAsync()
    {
        try
        {
            var keys = await _authClient.ListApiKeysAsync(User.GetUserId(), HttpContext.RequestAborted);
            Activity = await _billingClient.ListActivityByTokenAsync(
                keys.Select(key => key.Id).ToArray(),
                HttpContext.RequestAborted);
        }
        catch
        {
            ErrorMessage = "Failed to load activity data.";
        }
    }
}
