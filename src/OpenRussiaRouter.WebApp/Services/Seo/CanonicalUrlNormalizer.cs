namespace OpenRussiaRouter.WebApp.Services.Seo;

/// <summary>
/// Collapses trailing-slash and letter-case variants of public URLs onto a single canonical form.
/// Without it <c>/models</c>, <c>/models/</c> and <c>/Models</c> all return 200 and each emits a
/// self-referencing canonical, so the duplicates get reinforced instead of consolidated.
/// </summary>
public static class CanonicalUrlNormalizer
{
    /// <summary>
    /// Paths whose case is normalized. Deliberately a closed list: lower-casing every path would
    /// corrupt route values such as <c>/providers/logo/{id}</c>, <c>/tools/image/{guid}</c> and
    /// model slugs, which are compared ordinally downstream. Model detail URLs canonicalize
    /// themselves through <see cref="Catalog.ModelDetailRoute"/>.
    /// </summary>
    private static readonly HashSet<string> CaseNormalized = new(StringComparer.OrdinalIgnoreCase)
    {
        "/models",
        "/providers",
        "/tools",
        "/docs",
        "/support",
        "/legal/offer",
        "/legal/privacy",
    };

    /// <summary>
    /// Returns the canonical form of <paramref name="path"/>, or <c>null</c> when it is already
    /// canonical and no redirect is needed.
    /// </summary>
    public static string? TryNormalize(string? path)
    {
        // "/" is already canonical and must keep its single slash.
        if (string.IsNullOrEmpty(path) || path.Length == 1)
        {
            return null;
        }

        var trimmed = path.TrimEnd('/');

        // A path made only of slashes ("//") has no canonical form here — leave it to routing.
        if (trimmed.Length == 0)
        {
            return null;
        }

        // The set stores the canonical (lower-case) spelling, so a case-insensitive hit hands back
        // the exact form to redirect to.
        var canonical = CaseNormalized.TryGetValue(trimmed, out var known) ? known : trimmed;

        return string.Equals(canonical, path, StringComparison.Ordinal) ? null : canonical;
    }
}
