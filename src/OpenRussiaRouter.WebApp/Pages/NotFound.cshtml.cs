using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OpenRussiaRouter.WebApp.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class NotFoundModel : PageModel
{
    public void OnGet()
    {
        HttpContext.Response.StatusCode = 404;
    }
}
