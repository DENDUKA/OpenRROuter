using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

/// <summary>
/// Fetches and caches JWK keys from the Yandex Pay JWKS endpoint.
/// Handles key rotation: when an unknown kid is encountered, the cache is refreshed once.
/// </summary>
public sealed class YandexPayJwkProvider
{
    private const string SandboxJwksUrl = "https://sandbox.pay.yandex.ru/api/jwks";
    private const string ProductionJwksUrl = "https://pay.yandex.ru/api/jwks";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly YandexPaySettings _settings;
    private readonly ILogger<YandexPayJwkProvider> _logger;

    private readonly ConcurrentDictionary<string, CachedKeys> _cache = new(StringComparer.Ordinal);

    public YandexPayJwkProvider(
        IHttpClientFactory httpClientFactory,
        Microsoft.Extensions.Options.IOptions<PaymentSettings> options,
        ILogger<YandexPayJwkProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value.YandexPay;
        _logger = logger;
    }

    /// <summary>
    /// Get the signing keys. Returns a cached set when still valid; refreshes when expired
    /// or when <paramref name="knownKid"/> is not found (one retry after refresh).
    /// </summary>
    public async Task<IReadOnlyCollection<JsonWebKey>> GetKeysAsync(
        string? knownKid = null,
        CancellationToken ct = default)
    {
        var baseUrl = _settings.Sandbox ? SandboxJwksUrl : ProductionJwksUrl;
        var now = DateTimeOffset.UtcNow;

        // Try cache first
        if (_cache.TryGetValue(baseUrl, out var cached) && cached.ExpiresAt > now)
        {
            if (knownKid is null || cached.Keys.Any(k => k.Kid == knownKid))
            {
                return cached.Keys;
            }

            // Unknown kid — force refresh
            _logger.LogWarning(
                "JWK kid '{Kid}' not found in cache, refreshing from {Url}.",
                knownKid, baseUrl);
        }

        return await RefreshAsync(baseUrl, ct);
    }

    private async Task<IReadOnlyCollection<JsonWebKey>> RefreshAsync(string url, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("YandexPay");
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var jwks = JsonSerializer.Deserialize<YandexPayJwksResponse>(json);

        if (jwks is null || jwks.Keys.Length == 0)
        {
            throw new InvalidOperationException($"Yandex Pay JWKS endpoint returned no keys from {url}.");
        }

        var keys = new List<JsonWebKey>(jwks.Keys.Length);
        foreach (var element in jwks.Keys)
        {
            keys.Add(JsonWebKey.Create(element.GetRawText()));
        }

        var cacheEntry = new CachedKeys(keys, DateTimeOffset.UtcNow + _settings.JwkCacheDuration);
        _cache[url] = cacheEntry;

        _logger.LogInformation(
            "Loaded {Count} JWK keys from {Url}, cached until {ExpiresAt}.",
            keys.Count, url, cacheEntry.ExpiresAt);

        return keys;
    }

    private sealed class CachedKeys(IReadOnlyCollection<JsonWebKey> keys, DateTimeOffset expiresAt)
    {
        public IReadOnlyCollection<JsonWebKey> Keys { get; } = keys;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }
}
