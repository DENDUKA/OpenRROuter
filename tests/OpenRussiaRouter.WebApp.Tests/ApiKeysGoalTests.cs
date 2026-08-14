using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages.Workspace;
using OpenRussiaRouter.WebApp.Services.Analytics;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Goal reporting on the API keys page. The providers are left null on purpose: every load path
/// is wrapped in try/catch, so the page still renders and the assertions stay focused on which
/// goals were reported rather than on catalog data.
/// </summary>
public class ApiKeysGoalTests
{
    [Fact]
    public async Task OnGetAsync_TwoPendingGoals_ReportsBoth()
    {
        // Verifying an email and creating a key in the same visit used to be mutually exclusive:
        // the two goals shared one slot behind an "else if".
        var (model, _) = CreateModel();
        AnalyticsTempData.Enqueue(
            model.TempData,
            AnalyticsGoal.Named(AnalyticsGoals.EmailVerified),
            AnalyticsGoal.Named(AnalyticsGoals.ApiKeyCreated));

        await model.OnGetAsync();

        GoalsOn(model).Should().Equal(AnalyticsGoals.EmailVerified, AnalyticsGoals.ApiKeyCreated);
    }

    [Fact]
    public async Task OnGetAsync_NoPendingGoals_ReportsNothing()
    {
        var (model, _) = CreateModel();

        await model.OnGetAsync();

        model.ViewData[AnalyticsGoal.ViewDataKey].Should().BeNull();
    }

    [Fact]
    public async Task OnPostCreateAsync_ValidationError_LeavesPendingGoalUntouched()
    {
        // The error branch used to route through OnGetAsync, which consumed the pending goal and
        // reported a conversion on a page that is showing a validation error.
        var (model, _) = CreateModel();
        AnalyticsTempData.Enqueue(model.TempData, AnalyticsGoal.Named(AnalyticsGoals.EmailVerified));
        model.Create = new ApiKeysModel.CreateKeyInput { Name = "   " };

        await model.OnPostCreateAsync();

        GoalsOn(model).Should().BeEmpty("a failed submit reports nothing");
        AnalyticsTempData.Dequeue(model.TempData)
            .Select(g => g.Name)
            .Should().Equal([AnalyticsGoals.EmailVerified], "the goal must survive for the next render");
    }

    [Fact]
    public async Task OnGetAsync_AfterRedirect_ReportsGoalExactlyOnce()
    {
        var (model, scope) = CreateModel();
        AnalyticsTempData.Enqueue(model.TempData, AnalyticsGoal.Named(AnalyticsGoals.ApiKeyCreated));

        await model.OnGetAsync();
        GoalsOn(model).Should().Equal(AnalyticsGoals.ApiKeyCreated);

        var (refreshed, _) = CreateModel(scope.Next(model.TempData));
        await refreshed.OnGetAsync();
        refreshed.ViewData[AnalyticsGoal.ViewDataKey].Should().BeNull("F5 must not re-report it");
    }

    private static IReadOnlyList<string> GoalsOn(ApiKeysModel model) =>
        (model.ViewData[AnalyticsGoal.ViewDataKey] as IEnumerable<AnalyticsGoal>)?
            .Select(g => g.Name).ToList() ?? [];

    private static (ApiKeysModel Model, TempDataScope Scope) CreateModel(TempDataDictionary? tempData = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"))
        };

        var model = new ApiKeysModel(
            authProvider: default!,
            billingProvider: default!,
            controlPlaneProvider: default!,
            NullStringLocalizer<SharedResource>.Instance,
            NullLogger<ApiKeysModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext,
                ViewData = new ViewDataDictionary(
                    new EmptyModelMetadataProvider(), new ModelStateDictionary())
            }
        };

        var scope = new TempDataScope(httpContext);
        model.TempData = tempData ?? scope.Create();

        return (model, scope);
    }
}
