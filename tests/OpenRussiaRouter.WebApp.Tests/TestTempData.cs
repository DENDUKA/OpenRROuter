using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// In-memory <see cref="ITempDataProvider"/> so page-model unit tests can exercise the
/// POST-redirect-GET handoff without a cookie or data protection. Keeping one provider instance
/// across two dictionaries reproduces the real round trip: what one request saves, the next loads.
/// </summary>
internal sealed class InMemoryTempDataProvider : ITempDataProvider
{
    private Dictionary<string, object> _store = [];

    public IDictionary<string, object> LoadTempData(HttpContext context) =>
        new Dictionary<string, object>(_store, StringComparer.Ordinal);

    public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
        _store = new Dictionary<string, object>(values, StringComparer.Ordinal);
}

internal static class TestTempData
{
    public static TempDataDictionary Create(HttpContext context) =>
        new(context, new InMemoryTempDataProvider());

    /// <summary>Persists <paramref name="tempData"/> and returns what the next request would see.</summary>
    public static TempDataDictionary RoundTrip(TempDataDictionary tempData, HttpContext context)
    {
        var provider = new InMemoryTempDataProvider();
        provider.SaveTempData(context, tempData);
        var next = new TempDataDictionary(context, provider);
        next.Load();
        return next;
    }
}
