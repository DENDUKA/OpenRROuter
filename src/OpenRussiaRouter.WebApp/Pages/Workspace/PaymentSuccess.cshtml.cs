using Microsoft.AspNetCore.Authorization;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class PaymentSuccessModel : PageModelBase
{
    public void OnGet() { }
}
