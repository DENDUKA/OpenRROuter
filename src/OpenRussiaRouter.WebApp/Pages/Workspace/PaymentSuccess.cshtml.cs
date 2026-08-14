using Microsoft.AspNetCore.Authorization;
using OpenRussiaRouter.WebApp.Services.Analytics;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class PaymentSuccessModel : PageModelBase
{
    /// <summary>
    /// In production providers return to <c>/workspace/credits?topup=success</c> and this page is
    /// barely reachable, but the dev mock lands here — so it has to report goals too, otherwise a
    /// top-up cannot be exercised locally at all.
    /// </summary>
    public void OnGet() => TrackGoals(AnalyticsTempData.Dequeue(TempData));
}
