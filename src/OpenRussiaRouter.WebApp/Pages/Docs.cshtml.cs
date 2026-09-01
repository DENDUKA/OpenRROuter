using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Analytics;

namespace OpenRussiaRouter.WebApp.Pages;

public class DocsModel(ApiBaseUrlResolver baseUrlResolver) : PageModelBase
{
    /// <summary>User-facing API base URL (no trailing slash, without <c>/v1</c>).</summary>
    public string ApiBaseUrl { get; private set; } = "";

    public string V1BaseUrl => $"{ApiBaseUrl}/v1";
    public string ChatCompletionsUrl => $"{ApiBaseUrl}/v1/chat/completions";

    /// <summary>On the generic guide we don't know the model, so the snippets use a placeholder id.</summary>
    public IntegrationExamplesModel Integration => new(V1BaseUrl, "<paste-a-model-id>", 128000);

    public async Task OnGetAsync()
    {
        ApiBaseUrl = await baseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);
        TrackGoal(AnalyticsGoals.DocsView);
    }
}
