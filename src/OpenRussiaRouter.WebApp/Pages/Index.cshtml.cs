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

    public IndexModel(BillingProvider billingProvider, AuthProvider authProvider)
    {
        _billingProvider = billingProvider;
        _authProvider = authProvider;
    }

    public async Task OnGetAsync()
    {
        var user = CurrentUser;
        if (user != null)
        {
            try 
            {
                var balance = await _billingProvider.GetBalanceAsync(user.UserId);
                HasBalance = balance.Amount > 0;

                var keys = await _authProvider.ListApiKeysAsync(user.UserId);
                HasApiKey = keys.Count > 0;
            }
            catch 
            {
                // Ignore
            }
        }
    }
}
