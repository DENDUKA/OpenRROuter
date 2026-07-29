using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Services;

/// <summary>
/// Resolves the user-facing API base URL (no trailing slash, without <c>/v1</c>). Resolution order:
/// admin setting (<c>platform_settings</c>) → <c>PublicApiBaseUrl</c> config → current request origin.
/// The admin setting wins so prod/VPS (where WebApp and ApiGateway are on different hosts/ports) can be
/// configured without a redeploy; the origin fallback keeps single-host/ingress setups working.
/// The resolved value is cached for 5 minutes to avoid a gRPC round-trip on every page render.
/// Call <see cref="InvalidateCache"/> after saving the admin setting to reflect changes immediately.
/// </summary>
public sealed class ApiBaseUrlResolver(
    IConfiguration configuration,
    BillingProvider billingProvider,
    IMemoryCache cache,
    ILogger<ApiBaseUrlResolver> logger)
{
    private const string CacheKey = "ApiBaseUrlResolver:PublicApiBaseUrl";

    public async Task<string> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        if (cache.TryGetValue<string>(CacheKey, out var cached) && cached is not null)
            return cached;

        string? configured = null;
        try
        {
            configured = await billingProvider.GetPlatformSettingAsync(PlatformSettingKeys.PublicApiBaseUrl, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load public API base URL setting; falling back to config/origin.");
        }

        if (string.IsNullOrWhiteSpace(configured))
            configured = configuration["PublicApiBaseUrl"];

        var result = !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}";

        cache.Set(CacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Size = 1,
        });

        return result;
    }

    /// <summary>
    /// Removes the cached value so the next <see cref="ResolveAsync"/> call fetches from Billing.
    /// Called from the admin settings page after saving <c>PublicApiBaseUrl</c>.
    /// </summary>
    public void InvalidateCache() => cache.Remove(CacheKey);
}
