using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages;

public abstract class PageModelBase : PageModel
{
    public UserSession? CurrentUser => User.GetUserSession();
}
