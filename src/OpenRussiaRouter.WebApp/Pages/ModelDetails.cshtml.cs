using Microsoft.AspNetCore.Mvc;

namespace OpenRussiaRouter.WebApp.Pages;

public class ModelDetailsModel : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public string Provider { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = "";

    public string ModelId => $"{Provider}/{Slug}";
    public string ProviderTitle => ToTitle(Provider);
    public string DisplayName => ToTitle(Slug.Replace('-', ' '));
    public string Initials => string.Concat(ProviderTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public void OnGet() { }

    private static string ToTitle(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}
