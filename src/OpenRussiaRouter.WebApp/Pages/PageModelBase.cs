using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages;

public abstract class PageModelBase : PageModel
{
    public UserSession? CurrentUser => User.GetUserSession();

    /// <summary>
    /// Reports a conversion goal on this render. Calls accumulate — a second goal never replaces
    /// the first, which is the whole point: verifying an email and creating a key in one visit
    /// used to be mutually exclusive because the two shared a single slot.
    /// </summary>
    protected void TrackGoal(string name) => TrackGoal(AnalyticsGoal.Named(name));

    /// <inheritdoc cref="TrackGoal(string)"/>
    protected void TrackGoal(AnalyticsGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        if (ViewData[AnalyticsGoal.ViewDataKey] is not List<AnalyticsGoal> pending)
        {
            pending = [];
            ViewData[AnalyticsGoal.ViewDataKey] = pending;
        }

        pending.Add(goal);
    }

    /// <inheritdoc cref="TrackGoal(string)"/>
    protected void TrackGoals(IEnumerable<AnalyticsGoal> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);

        foreach (var goal in goals)
            TrackGoal(goal);
    }
}
