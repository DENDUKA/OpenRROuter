namespace OpenRussiaRouter.WebApp.Services.Catalog;

/// <summary>
/// Single source of truth for the <c>/models/{provider}/{slug}</c> URL contract: the catalog listing,
/// the provider page, the sitemap and the detail page itself must all agree on it.
/// </summary>
/// <remarks>
/// Canonical form is <c>{provider} = ModelInfo.ProviderId</c> and <c>{slug} =</c> the model id after
/// its last slash — the public id is <b>not</b> <c>"{provider}/{slug}"</c>: the catalog holds both bare
/// ids (<c>claude-opus-4-8</c>) and prefixed ones (<c>ORR/Gemini-3.1-Pro</c>), so the id can never be
/// reassembled from the two route segments. <see cref="Matches"/> additionally accepts the historical
/// segments (provider display name, the id prefix, the whole id in the slug) so that already-published
/// links resolve and get redirected onto the canonical URL instead of 404-ing.
/// </remarks>
public static class ModelDetailRoute
{
    /// <summary>The <c>{slug}</c> route value of a model id: everything after the last slash.</summary>
    public static string SlugOf(string? modelId)
    {
        var id = (modelId ?? string.Empty).Trim();
        var lastSlash = id.LastIndexOf('/');
        return lastSlash >= 0 ? id[(lastSlash + 1)..] : id;
    }

    /// <summary>
    /// Turns a raw <c>{slug}</c> route value into the slug this contract compares against. Links built
    /// before the contract existed put the whole id into the segment, so the slash arrives either
    /// decoded or still percent-encoded depending on how the request was written.
    /// </summary>
    public static string NormalizeSlug(string? routeValue)
    {
        var value = routeValue ?? string.Empty;
        if (value.Contains('%', StringComparison.Ordinal))
        {
            value = Uri.UnescapeDataString(value);
        }

        return SlugOf(value);
    }

    /// <summary>
    /// The canonical <c>{provider}</c> route value: the provider id, else the prefix of a single-slash
    /// id, else the provider display name. <c>null</c> when the model has no addressable provider
    /// segment at all — such a model has no detail URL and must be left unlinked.
    /// </summary>
    public static string? ProviderSegmentOf(string? modelId, string? providerId, string? providerDisplayName)
    {
        var id = (modelId ?? string.Empty).Trim();
        var firstSlash = id.IndexOf('/');
        var idPrefix = firstSlash > 0 && firstSlash < id.Length - 1 && firstSlash == id.LastIndexOf('/')
            ? id[..firstSlash]
            : null;

        return Sanitize(providerId) ?? Sanitize(idPrefix) ?? Sanitize(providerDisplayName);
    }

    /// <summary>
    /// The canonical relative path of a model's detail page, or <c>null</c> when the model cannot be
    /// addressed (no slug, or no usable provider segment).
    /// </summary>
    public static string? TryBuildPath(string? modelId, string? providerId, string? providerDisplayName)
    {
        var slug = SlugOf(modelId);
        if (slug.Length == 0)
        {
            return null;
        }

        var provider = ProviderSegmentOf(modelId, providerId, providerDisplayName);
        return provider is null
            ? null
            : $"/models/{Uri.EscapeDataString(provider)}/{Uri.EscapeDataString(slug)}";
    }

    /// <summary>
    /// True when the requested route values are already the canonical ones (byte-for-byte, so that
    /// a differently-cased or legacy segment is redirected rather than served as duplicate content).
    /// </summary>
    public static bool IsCanonical(
        string? modelId,
        string? providerId,
        string? providerDisplayName,
        string? providerSegment,
        string? slugSegment) =>
        ProviderSegmentOf(modelId, providerId, providerDisplayName) is { } provider
        && string.Equals(provider, providerSegment, StringComparison.Ordinal)
        && string.Equals(SlugOf(modelId), slugSegment, StringComparison.Ordinal);

    /// <summary>
    /// True when the route values address this catalog entry in any accepted form: the slug must match
    /// the model's slug, and the provider segment must be its id, its display name or its id prefix.
    /// </summary>
    public static bool Matches(
        string? modelId,
        string? providerId,
        string? providerDisplayName,
        string? providerSegment,
        string? slugSegment)
    {
        if (!string.Equals(SlugOf(modelId), NormalizeSlug(slugSegment), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segment = (providerSegment ?? string.Empty).Trim();
        if (segment.Length == 0)
        {
            return false;
        }

        var id = (modelId ?? string.Empty).Trim();
        var firstSlash = id.IndexOf('/');
        var idPrefix = firstSlash > 0 ? id[..firstSlash] : null;

        return Equal(segment, providerId)
            || Equal(segment, providerDisplayName)
            || Equal(segment, idPrefix);
    }

    private static bool Equal(string segment, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && string.Equals(segment, candidate.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A route segment cannot carry a slash: percent-encoding it would produce a <c>%2F</c> that the
    /// two-segment route never matches, so such a value is unusable as a provider segment.
    /// </summary>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Contains('/', StringComparison.Ordinal) ? null : trimmed;
    }
}
