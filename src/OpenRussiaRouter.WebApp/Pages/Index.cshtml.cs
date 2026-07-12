using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Auth;
using System.Threading.Tasks;

namespace OpenRussiaRouter.WebApp.Pages;

public class IndexModel : PageModelBase 
{
    private readonly BillingProvider _billingProvider;
    private readonly AuthProvider _authProvider;

    public bool HasBalance { get; set; }
    public bool HasApiKey { get; set; }
    public IReadOnlyList<ProviderInfo> Providers { get; private set; } = [];

    public bool StartBalanceEnabled { get; set; }
    public decimal StartBalanceAmountUsd { get; set; }
    public bool FirstTopUpEnabled { get; set; }
    public decimal FirstTopUpPercent { get; set; }
    public bool HasPaidTopUp { get; set; }
    public bool FirstTopUpBonusAvailable { get; set; }

    public IndexModel(BillingProvider billingProvider, AuthProvider authProvider)
    {
        _billingProvider = billingProvider;
        _authProvider = authProvider;
    }

    public async Task OnGetAsync()
    {
        try
        {
            Providers = await _billingProvider.ListModelProvidersAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // Ignore — fallback to empty strip
        }

        try
        {
            (StartBalanceEnabled, StartBalanceAmountUsd, FirstTopUpEnabled, FirstTopUpPercent) =
                await _billingProvider.GetPromotionSettingsAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // Ignore — promo blocks hidden on error
        }

        var user = CurrentUser;
        if (user != null)
        {
            try 
            {
                var balance = await _billingProvider.GetBalanceAsync(user.UserId);
                HasBalance = balance.Amount > 0;

                var keys = await _authProvider.ListApiKeysAsync(user.UserId);
                HasApiKey = keys.Count > 0;

                HasPaidTopUp = await _billingProvider.HasUserHadPaidTopUpAsync(user.UserId, HttpContext.RequestAborted);
                FirstTopUpBonusAvailable = FirstTopUpEnabled && !HasPaidTopUp;
            }
            catch 
            {
                // Ignore
            }
        }
    }
}
