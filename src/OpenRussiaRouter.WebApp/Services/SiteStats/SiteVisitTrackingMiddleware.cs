using Microsoft.Extensions.Options;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Services.SiteStats;

/// <summary>
/// Records one row in <c>analytics.site_visits</c> per rendered page, so the Grafana
/// «ORR Users» dashboard can show unique visitors — including the ones who never sign up —
/// and where they came from.
/// </summary>
/// <remarks>
/// The visit is staged only after the response is produced and only when it turned out to
/// be an HTML page: that filters static files, redirects and API/webhook calls without
/// maintaining a list of extensions. Nothing here can fail the request — the queue drops
/// writes when full, and any unexpected error is swallowed after logging.
/// </remarks>
public sealed class SiteVisitTrackingMiddleware
{
    private static readonly string[] IgnoredPrefixes =
        ["/health", "/metrics", "/internal", "/webhook", "/.well-known"];

    private static readonly string[] BotMarkers =
        ["bot", "crawler", "spider", "slurp", "curl", "wget", "python-requests",
         "headless", "monitor", "uptime", "scrapy", "httpclient", "postman", "preview"];

    private static readonly string[] SearchHosts =
        ["google.", "yandex.", "bing.", "duckduckgo.", "search.", "mail.ru", "rambler.", "baidu."];

    private static readonly string[] SocialHosts =
        ["t.me", "telegram", "vk.com", "vk.ru", "habr.", "reddit.", "twitter.", "x.com",
         "facebook.", "instagram.", "linkedin.", "youtube.", "dzen.", "pikabu."];

    private readonly RequestDelegate _next;

    public SiteVisitTrackingMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(
        HttpContext context,
        SiteVisitQueue queue,
        VisitorIdHasher hasher,
        UserPseudonymizer pseudonymizer,
        IOptions<ProductAnalyticsOptions> analyticsOptions,
        ILogger<SiteVisitTrackingMiddleware> logger)
    {
        var trackable = IsTrackableRequest(context);

        await _next(context).ConfigureAwait(false);

        if (!trackable || !IsHtmlResponse(context))
        {
            return;
        }

        try
        {
            queue.TryEnqueue(BuildVisit(context, hasher, pseudonymizer, analyticsOptions.Value.Environment));
        }
        catch (Exception ex)
        {
            // Traffic accounting must never surface to the visitor.
            logger.LogWarning(ex, "Failed to record a site visit");
        }
    }

    private static bool IsTrackableRequest(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        foreach (var prefix in IgnoredPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHtmlResponse(HttpContext context) =>
        context.Response.StatusCode < 400 &&
        context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

    private static SiteVisit BuildVisit(
        HttpContext context,
        VisitorIdHasher hasher,
        UserPseudonymizer pseudonymizer,
        string? environment)
    {
        var request = context.Request;
        var userAgent = request.Headers.UserAgent.ToString();
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;

        string? distinctId = null;
        if (isAuthenticated)
        {
            var userId = context.User.GetUserId();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                distinctId = pseudonymizer.ComputeDistinctId(userId);
            }
        }

        var (referrerHost, referrerKind) = ClassifyReferrer(request.Headers.Referer.ToString(), request.Host.Host);

        return new SiteVisit(
            hasher.ComputeVisitorHash(context.Connection.RemoteIpAddress?.ToString(), userAgent),
            distinctId,
            DateTimeOffset.UtcNow,
            NormalizePath(request.Path.Value),
            referrerHost,
            referrerKind,
            ReadQueryValue(request, "utm_source"),
            ReadQueryValue(request, "utm_medium"),
            ReadQueryValue(request, "utm_campaign"),
            ClassifyDevice(userAgent),
            isAuthenticated,
            environment ?? string.Empty);
    }

    /// <summary>
    /// Collapses id-like segments (<c>/models/ORR%2FGLM-4.7</c>, <c>/invite/{token}</c>) so
    /// the landing-page panels stay readable instead of exploding into one row per id.
    /// </summary>
    internal static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            if (LooksLikeIdentifier(segments[i]))
            {
                segments[i] = "{id}";
            }
        }

        var normalized = "/" + string.Join('/', segments);
        return normalized.Length > 200 ? normalized[..200] : normalized;
    }

    private static bool LooksLikeIdentifier(string segment)
    {
        if (segment.Length >= 16)
        {
            return true;
        }

        foreach (var c in segment)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return segment.Length > 0;
    }

    /// <summary>
    /// Returns the referrer host and its coarse bucket. Only the host is kept — the rest of
    /// the referring URL may carry someone else's query parameters and is dropped.
    /// </summary>
    internal static (string? Host, string Kind) ClassifyReferrer(string? referer, string? ownHost)
    {
        if (string.IsNullOrWhiteSpace(referer) ||
            !Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        {
            return (null, SiteVisitReferrerKinds.Direct);
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Length == 0)
        {
            return (null, SiteVisitReferrerKinds.Direct);
        }

        if (!string.IsNullOrEmpty(ownHost) &&
            host.Equals(ownHost, StringComparison.OrdinalIgnoreCase))
        {
            return (host, SiteVisitReferrerKinds.Internal);
        }

        if (ContainsAny(host, SearchHosts))
        {
            return (host, SiteVisitReferrerKinds.Search);
        }

        if (ContainsAny(host, SocialHosts))
        {
            return (host, SiteVisitReferrerKinds.Social);
        }

        return (host, SiteVisitReferrerKinds.External);
    }

    internal static string ClassifyDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            // No user agent at all is far more typical of a script than of a browser.
            return SiteVisitDeviceClasses.Bot;
        }

        var ua = userAgent.ToLowerInvariant();

        if (ContainsAny(ua, BotMarkers))
        {
            return SiteVisitDeviceClasses.Bot;
        }

        if (ua.Contains("ipad", StringComparison.Ordinal) || ua.Contains("tablet", StringComparison.Ordinal))
        {
            return SiteVisitDeviceClasses.Tablet;
        }

        if (ua.Contains("mobi", StringComparison.Ordinal) ||
            ua.Contains("android", StringComparison.Ordinal) ||
            ua.Contains("iphone", StringComparison.Ordinal))
        {
            return SiteVisitDeviceClasses.Mobile;
        }

        return SiteVisitDeviceClasses.Desktop;
    }

    private static bool ContainsAny(string value, string[] markers)
    {
        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadQueryValue(HttpRequest request, string key)
    {
        if (!request.Query.TryGetValue(key, out var values))
        {
            return null;
        }

        var value = values.ToString().Trim();
        if (value.Length == 0)
        {
            return null;
        }

        value = value.ToLowerInvariant();
        return value.Length > 100 ? value[..100] : value;
    }
}
