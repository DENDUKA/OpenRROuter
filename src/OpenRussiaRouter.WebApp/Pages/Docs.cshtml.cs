using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;

namespace OpenRussiaRouter.WebApp.Pages;

public sealed record DocsTool(
    string Slug,
    string IconLetter,
    string AccentColor,
    string CategoryKey,
    string NameKey,
    string ShortKey,
    string[] FeatureKeys);

public class DocsModel(ApiBaseUrlResolver baseUrlResolver) : PageModelBase
{
    /// <summary>User-facing API base URL (no trailing slash, without <c>/v1</c>).</summary>
    public string ApiBaseUrl { get; private set; } = "";

    public string V1BaseUrl => $"{ApiBaseUrl}/v1";
    public string ChatCompletionsUrl => $"{ApiBaseUrl}/v1/chat/completions";

    /// <summary>Catalog cards rendered on <c>/docs</c>.</summary>
    public IReadOnlyList<DocsTool> Tools => Docs.DocsCatalog.All;

    public async Task OnGetAsync()
    {
        ApiBaseUrl = await baseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);
    }
}
