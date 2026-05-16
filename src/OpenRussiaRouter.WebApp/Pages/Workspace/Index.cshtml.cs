using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class IndexModel : PageModelBase
{
    public IActionResult OnGet() => RedirectToPage("/Workspace/ApiKeys");
}
