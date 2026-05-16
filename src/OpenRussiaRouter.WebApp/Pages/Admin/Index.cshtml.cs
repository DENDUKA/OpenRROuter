using Microsoft.AspNetCore.Authorization;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModelBase
{
    public void OnGet() { }
}
