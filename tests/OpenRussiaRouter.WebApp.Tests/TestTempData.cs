using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// In-memory <see cref="ITempDataProvider"/> so page-model unit tests can exercise the
/// POST-redirect-GET handoff without cookies or data protection.
/// </summary>
internal sealed class InMemoryTempDataProvider : ITempDataProvider
{
    private Dictionary<string, object> _store = [];

    public IDictionary<string, object> LoadTempData(HttpContext context) =>
        new Dictionary<string, object>(_store, StringComparer.Ordinal);

    public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
        _store = new Dictionary<string, object>(values, StringComparer.Ordinal);
}

/// <summary>
/// One backing store shared by successive <see cref="TempDataDictionary"/> instances, so
/// <see cref="Next"/> reproduces a real redirect: only keys the previous request left unread
/// survive, which is exactly what makes a queued goal fire once and not twice.
/// </summary>
internal sealed class TempDataScope(HttpContext context)
{
    private readonly InMemoryTempDataProvider _provider = new();

    public TempDataDictionary Create()
    {
        var tempData = new TempDataDictionary(context, _provider);
        tempData.Load();
        return tempData;
    }

    /// <summary>Saves <paramref name="current"/> and returns what the next request would see.</summary>
    public TempDataDictionary Next(ITempDataDictionary current)
    {
        current.Save();
        return Create();
    }
}
