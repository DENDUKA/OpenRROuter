using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>Captures the consented Yandex Metrica ClientID for authenticated users.</summary>
public sealed partial class YandexMetrikaIdentityMiddleware(
    RequestDelegate next,
    IYandexMetrikaIdentityStore identityStore,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<YandexMetrikaIdentityMiddleware> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    /// <summary>Captures a valid identity before continuing the HTTP pipeline.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.GetUserId()
            : null;
        var hasConsent = context.Request.Cookies.TryGetValue("orr_consent", out var consent)
            && string.Equals(consent, "1", StringComparison.Ordinal);
        var hasClientId = context.Request.Cookies.TryGetValue("_ym_uid", out var clientId)
            && clientId is not null
            && ClientIdRegex().IsMatch(clientId);

        if (!string.IsNullOrWhiteSpace(userId) && hasConsent && hasClientId)
        {
            var cacheKey = $"ym-client-id:{userId}:{clientId}";
            if (!cache.TryGetValue(cacheKey, out _))
            {
                try
                {
                    await identityStore.UpsertAsync(
                        userId,
                        clientId!,
                        timeProvider.GetUtcNow(),
                        context.RequestAborted).ConfigureAwait(false);
                    cache.Set(cacheKey, true, CacheDuration);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to persist Yandex Metrica identity.");
                }
            }
        }

        await next(context).ConfigureAwait(false);
    }

    [GeneratedRegex("^[0-9]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdRegex();
}
