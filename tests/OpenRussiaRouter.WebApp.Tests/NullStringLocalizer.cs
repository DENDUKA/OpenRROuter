using Microsoft.Extensions.Localization;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Minimal no-op <see cref="IStringLocalizer{T}"/> for tests that don't care about
/// translated strings. Returns the resource key as-is.
/// </summary>
internal sealed class NullStringLocalizer<TResourceSource> : IStringLocalizer<TResourceSource>
{
    public static readonly NullStringLocalizer<TResourceSource> Instance = new();

    private static readonly LocalizedString[] _all = [new(string.Empty, string.Empty, resourceNotFound: true)];

    public LocalizedString this[string name] => new(name, name, resourceNotFound: true);
    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _all;

    [Obsolete("This method is obsolete. Use `CurrentCulture` and `CurrentUICulture` instead.")]
    public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
}
