using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Pages.Shared;
using OpenRussiaRouter.WebApp.Services;

namespace OpenRussiaRouter.WebApp.Pages.Docs;

/// <summary>
/// Base for <c>/docs/{slug}</c> detail pages. Resolves the API base URL
/// and exposes helpers to build the layout model from per-tool data.
/// </summary>
public abstract class DocsToolPageModel(ApiBaseUrlResolver baseUrlResolver) : PageModelBase
{
    protected ApiBaseUrlResolver BaseUrlResolver { get; } = baseUrlResolver;

    public string ApiBaseUrl { get; private set; } = "";
    public string V1BaseUrl => $"{ApiBaseUrl}/v1";
    public string ModelId { get; protected set; } = "<paste-a-model-id>";

    public DocsToolLayoutModel Layout { get; protected set; } = new();

    public virtual async Task OnGetAsync()
    {
        ApiBaseUrl = await BaseUrlResolver.ResolveAsync(Request, HttpContext.RequestAborted);
        Layout = BuildLayout();
    }

    /// <summary>Override in derived classes to populate <see cref="Layout"/>.</summary>
    protected abstract DocsToolLayoutModel BuildLayout();

    /// <summary>Sidebar list of other tools (the catalog minus the current slug).</summary>
    protected static IReadOnlyList<DocsTool> OtherToolsFor(string slug) =>
        DocsCatalog.All.Where(t => t.Slug != slug).ToArray();
}
