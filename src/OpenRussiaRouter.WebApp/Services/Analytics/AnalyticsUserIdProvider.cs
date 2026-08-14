using Microsoft.Extensions.Options;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>
/// Produces the pseudonymous id reported to the counters as <c>setUserID</c> / <c>user_id</c>.
/// </summary>
/// <remarks>
/// Only the HMAC pseudonym ever leaves the process — never the internal user id and never the
/// email. What it buys is cross-device stitching: without it the signup → top-up funnel is
/// measured per browser cookie and falls apart the moment someone registers on a laptop and pays
/// on a phone. Rotating <c>ProductAnalytics:IdentitySalt</c> severs the link to every profile
/// already recorded in the cabinets.
/// <para>
/// The wrapper exists because <see cref="UserPseudonymizer"/> throws on an empty salt, and the
/// salt is empty in dev and in tests. Resolving it eagerly from the layout would take down every
/// page rather than quietly skipping the id, so construction is deferred and guarded.
/// </para>
/// </remarks>
public sealed class AnalyticsUserIdProvider
{
    private readonly Lazy<UserPseudonymizer?> _pseudonymizer;

    public AnalyticsUserIdProvider(IOptions<ProductAnalyticsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pseudonymizer = new Lazy<UserPseudonymizer?>(() =>
            string.IsNullOrWhiteSpace(options.Value.IdentitySalt)
                ? null
                : new UserPseudonymizer(options.Value));
    }

    /// <summary>Pseudonym for <paramref name="userId"/>, or null when there is no user or no salt.</summary>
    public string? Compute(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? null : _pseudonymizer.Value?.ComputeDistinctId(userId);
}
