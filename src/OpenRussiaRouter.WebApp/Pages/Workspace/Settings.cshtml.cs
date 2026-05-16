using Microsoft.AspNetCore.Authorization;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class SettingsModel : PageModelBase
{
    public void OnGet() { }
}
