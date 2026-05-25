using System.Collections.Concurrent;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Http;

public sealed class RouterNodeProviderCacheStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet<TValue>(string key, DateTimeOffset nowUtc, out TValue value)
    {
        value = default!;

        if (!_entries.TryGetValue(key, out var entry))
            return false;

        if (entry.ExpiresAtUtc <= nowUtc)
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        if (entry.Value is not TValue typedValue)
            return false;

        value = typedValue;
        return true;
    }

    public void Set<TValue>(string key, TValue value, DateTimeOffset expiresAtUtc)
    {
        _entries[key] = new CacheEntry(value!, expiresAtUtc);
    }

    public void InvalidateNode(string nodeId)
    {
        var prefix = CreateNodePrefix(nodeId);
        foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            _entries.TryRemove(key, out _);
        }
    }

    public static string CreateNodePrefix(string nodeId) => nodeId + "::";

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAtUtc);
}
