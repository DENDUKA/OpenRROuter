namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>
/// The single source of truth for web-analytics goal names (Yandex Metrika <c>reachGoal</c> and
/// GA4 <c>event</c>). Nothing in the codebase may spell a goal name inline — the counters match
/// on the exact string, and a typo is invisible to the compiler and silent in the reports.
/// </summary>
/// <remarks>
/// Declaring a name here does not create the goal: both cabinets need it registered by hand as a
/// JavaScript event. Keep <c>docs/seo/webmasters-analytics-runbook.md</c> §7 in sync — it is the
/// checklist a human works from, and this class is what it mirrors.
/// <para>
/// Not to be confused with <c>BuildingBlocks.Analytics.AnalyticsEvents</c>: that is the internal
/// product-analytics pipeline writing to <c>analytics.events</c>, this is third-party web
/// analytics. The two deliberately do not share names or shapes.
/// </para>
/// </remarks>
public static class AnalyticsGoals
{
    // ── Macro funnel ────────────────────────────────────────────────────────────
    // These four are the conversion path and the only ones marked as conversions in the
    // cabinets. All are emitted server-side, so none of them can be lost to a slow tag load.

    /// <summary>Registration submitted (password form or a fresh external-provider account).</summary>
    public const string Signup = "signup";

    /// <summary>Account activated. The external-provider branch reaches it in the same render as
    /// <see cref="Signup"/> — the provider already vouched for the mailbox.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>First and every subsequent API key created. This is what counts as activation.</summary>
    public const string ApiKeyCreated = "api_key_created";

    /// <summary>Top-up paid. Carries the charged amount as goal value.</summary>
    public const string TopUpCompleted = "topup_completed";

    // ── Micro goals: landing and catalog CTA ────────────────────────────────────
    // The three signup CTAs sit on one page, so they need distinct names — otherwise there is
    // no way to tell which block actually earns registrations.

    public const string CtaSignupHero = "cta_signup_hero";
    public const string CtaSignupQuickstart = "cta_signup_quickstart";
    public const string CtaSignupFinal = "cta_signup_final";
    public const string CtaModelsHero = "cta_models_hero";
    public const string CtaModelsCatalog = "cta_models_catalog";
    public const string CtaModelsFinal = "cta_models_final";
    public const string CtaApiKeyFromModel = "cta_apikey_model";
    public const string CtaApiKeyFromProvider = "cta_apikey_provider";

    // ── Micro goals: integration intent ─────────────────────────────────────────
    // Copying an id, a code sample or the base URL is the strongest pre-registration signal
    // this product has: it means someone is about to wire us into their code.

    public const string CopyModelId = "copy_model_id";
    public const string CopyCodeSample = "copy_code_sample";
    public const string CopyBaseUrl = "copy_base_url";
    public const string CopyApiKey = "copy_api_key";

    // ── Micro goals: auth, payment, support ─────────────────────────────────────
    // One name per OAuth provider, not per page: both cabinets already split a goal by the URL
    // it was reached on, so /login and /signup stay distinguishable for free.

    public const string CtaOauthGoogle = "cta_oauth_google";
    public const string CtaOauthYandex = "cta_oauth_yandex";

    /// <summary>Checkout submitted. Fires on intent, so it counts attempts billing later refused.</summary>
    public const string CheckoutStarted = "checkout_started";

    public const string SupportSubmitted = "support_submitted";

    /// <summary>Every declared goal. Used by tests and when reconciling against the cabinets.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Signup,
        EmailVerified,
        ApiKeyCreated,
        TopUpCompleted,
        CtaSignupHero,
        CtaSignupQuickstart,
        CtaSignupFinal,
        CtaModelsHero,
        CtaModelsCatalog,
        CtaModelsFinal,
        CtaApiKeyFromModel,
        CtaApiKeyFromProvider,
        CopyModelId,
        CopyCodeSample,
        CopyBaseUrl,
        CopyApiKey,
        CtaOauthGoogle,
        CtaOauthYandex,
        CheckoutStarted,
        SupportSubmitted,
    ];

    /// <summary>The four goals that are conversions. Micro goals must stay out: marking them
    /// would drag the cabinets' own conversion rate down to meaninglessness.</summary>
    public static IReadOnlyList<string> Macro { get; } =
    [
        Signup,
        EmailVerified,
        ApiKeyCreated,
        TopUpCompleted,
    ];

    public static bool IsKnown(string name) => All.Contains(name, StringComparer.Ordinal);
}
