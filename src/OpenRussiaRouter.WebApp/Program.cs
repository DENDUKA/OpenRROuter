using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Grpc.Net.ClientFactory;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1.ContentService;
using OpenRussiaRouter.WebApp.Services.Content;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.Enrichment;
using OpenRussiaRouter.WebApp.Api;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Payments.Rollypay;
using OpenRussiaRouter.WebApp.Services.Currency;
using OpenRussiaRouter.WebApp.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using AspNet.Security.OAuth.Yandex;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using OpenRussiaRouter.WebApp.Services.Localization;
using OpenRussiaRouter.WebApp.Services.SiteStats;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("webapp");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.WebApp");

// Analytics sink (orr_analytics): gives the app the shared UserPseudonymizer and the
// Npgsql data source. Site visit tracking rides on top of it and stays off when the sink
// is not configured — see SiteVisitServiceCollectionExtensions.
builder.Services.AddProductAnalytics(builder.Configuration);
builder.Services.AddSiteVisitTracking(builder.Configuration);

// Visitor-history reader for /admin/visitors. Resolves to a disabled instance when the
// analytics sink is not configured (no NpgsqlDataSource in DI) — the page renders a
// banner instead of throwing.
builder.Services.AddSingleton<SiteVisitReader>(sp =>
{
    var dataSource = sp.GetService<Npgsql.NpgsqlDataSource>();
    var pseudonymizer = sp.GetService<UserPseudonymizer>();
    var logger = sp.GetRequiredService<ILogger<SiteVisitReader>>();
    return new SiteVisitReader(dataSource, pseudonymizer, logger);
});

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(o =>
        o.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var cultures = SupportedCultures.All;
    o.DefaultRequestCulture = new RequestCulture(SupportedCultures.DefaultCulture);
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});

var authenticationBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    })
    .AddCookie("External", o => o.Cookie.Name = ".orr.ext");

// Register Google external login only when credentials are configured.
// Without this guard the OAuth handler fails validation ("ClientId option must be provided")
// the moment the authentication middleware initializes the scheme, returning 500 on /Login.
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.SignInScheme = "External";
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = "/signin-google";
        options.Scope.Add("email");
    });
}

// Register Yandex external login only when credentials are configured.
var yandexClientId = builder.Configuration["Yandex:ClientId"];
var yandexClientSecret = builder.Configuration["Yandex:ClientSecret"];
if (!string.IsNullOrWhiteSpace(yandexClientId) && !string.IsNullOrWhiteSpace(yandexClientSecret))
{
    authenticationBuilder.AddYandex(options =>
    {
        options.SignInScheme = "External";
        options.ClientId = yandexClientId;
        options.ClientSecret = yandexClientSecret;
        options.CallbackPath = "/OAuth-yandex";
        options.Scope.Add("login:info");
        options.Scope.Add("login:email");
    });
}

builder.Services.AddAuthorization();

// Data Protection: persist the key ring to a stable location so antiforgery tokens and
// the cookie-auth session ticket survive container/pod restarts. Without this the keys
// live in the ephemeral container FS and every restart silently generates a new ring —
// logged-in users get kicked out (the auth ticket is protected with these keys) and
// leftover antiforgery cookies throw AntiforgeryValidationException ("The key {...} was
// not found in the key ring"). When DataProtection:KeyRingPath is unset, the framework
// default (ephemeral store) is used — handy for local dotnet run and unit tests.
var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("OpenRussiaRouter.WebApp");
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

// Default per-call deadline for all gRPC clients (overridable via GrpcServices:CallDeadlineSeconds).
var grpcCallDeadline = TimeSpan.FromSeconds(
    builder.Configuration.GetValue("GrpcServices:CallDeadlineSeconds", 30));

// TS-003: s2s credentials for outbound BillingService calls from WebApp.
var billingS2sCaller = builder.Configuration["ServiceAuth:BillingCaller"] ?? "webappadmin";
var billingS2sToken = builder.Configuration["ServiceAuth:BillingToken"];
if (string.IsNullOrWhiteSpace(billingS2sToken))
{
    throw new InvalidOperationException(
        "ServiceAuth:BillingToken is required for WebApp. Configure the shared s2s token in appsettings or a secret store.");
}

var authGrpcUrl = builder.Configuration["GrpcServices:Auth"] ?? "http://localhost:5001";
builder.Services.AddGrpcClient<AuthGrpc.AuthServiceClient>(options =>
{
    options.Address = new Uri(authGrpcUrl);
})
.ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    };
})
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
builder.Services.AddScoped<AuthProvider>();

var billingGrpcUrl = builder.Configuration["GrpcServices:Billing"] ?? "http://localhost:5002";
builder.Services.AddGrpcClient<BillingGrpc.BillingServiceClient>(options =>
{
    options.Address = new Uri(billingGrpcUrl);
})
.ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    };
})
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline))
.AddInterceptor(() => new ServiceAuthClientInterceptor(billingS2sCaller, billingS2sToken));
builder.Services.AddScoped<BillingProvider>();
builder.Services.AddScoped<OpenRussiaRouter.WebApp.Services.ApiBaseUrlResolver>();

// ContentService owns the /tools catalog. Its admin surface is s2s-protected with the same
// webappadmin credentials as Billing, so no new secret material is introduced.
var contentGrpcUrl = builder.Configuration["GrpcServices:Content"] ?? "http://localhost:5007";
var contentS2sCaller = builder.Configuration["ServiceAuth:ContentCaller"] ?? billingS2sCaller;
var contentS2sToken = builder.Configuration["ServiceAuth:ContentToken"] ?? billingS2sToken;
builder.Services.AddGrpcClient<ContentGrpc.ContentServiceClient>(options =>
{
    options.Address = new Uri(contentGrpcUrl);
})
.ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    };
    // Tool images are capped at 1 MiB; leave headroom over the 4 MiB default for framing.
    options.MaxSendMessageSize = 8 * 1024 * 1024;
    options.MaxReceiveMessageSize = 8 * 1024 * 1024;
})
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline))
.AddInterceptor(() => new ServiceAuthClientInterceptor(contentS2sCaller, contentS2sToken));
builder.Services.AddScoped<ContentProvider>();

var controlPlaneGrpcUrl = builder.Configuration["GrpcServices:ControlPlane"] ?? "http://localhost:5003";
builder.Services.AddGrpcClient<ControlGrpc.RouterControlPlaneClient>(options =>
{
    options.Address = new Uri(controlPlaneGrpcUrl);
})
.ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    };
})
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
builder.Services.AddScoped<ControlPlaneProvider>();
builder.Services.AddScoped<RouterNodeApiProvider>();

var collectorGrpcUrl = builder.Configuration["GrpcServices:Collector"] ?? "http://localhost:5005";
builder.Services.AddGrpcClient<OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector.CollectorClient>(options =>
{
    options.Address = new Uri(collectorGrpcUrl);
})
.ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    };
})
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
builder.Services.AddScoped<CollectorProvider>();

var apiGatewayUrl = builder.Configuration["ApiGatewayUrl"] ?? "http://localhost:5000";
builder.Services.AddHttpClient("ApiGateway", client =>
{
    client.BaseAddress = new Uri(apiGatewayUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(10),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
});

builder.Services.AddHttpClient("RouterNode", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(10),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
});

builder.Services.AddHttpClient("DirectNodeChat", client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(10),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
});

builder.Services.AddHttpClient("OpenRouterDirect", client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IOpenRouterCatalogClient, OpenRouterCatalogClient>();

builder.Services.AddEmailInfrastructure(builder.Configuration);

// Customer support: contact form relays to a staff inbox via the email queue; optional
// Telegram link is surfaced as an alternative channel.
builder.Services.Configure<OpenRussiaRouter.WebApp.Support.SupportSettings>(
    builder.Configuration.GetSection(OpenRussiaRouter.WebApp.Support.SupportSettings.SectionName));

builder.Services.AddSingleton<OpenRussiaRouter.WebApp.Services.Legal.LegalDocumentService>();

// TS-005: display-only currency layer (CBR rates, RUB/USD/EUR)
builder.Services.Configure<ExchangeRateSettings>(
    builder.Configuration.GetSection(ExchangeRateSettings.SectionName));
builder.Services.AddSingleton<CbrRateSnapshot>();
builder.Services.AddHttpClient("Cbr");
builder.Services.AddSingleton<CbrClient>();
builder.Services.AddSingleton<IExchangeRateProvider, CbrExchangeRateProvider>();
builder.Services.AddSingleton<CurrencyFormatter>();
builder.Services.AddHostedService<CbrRateRefresher>();

// TS-004: payments — provider-agnostic abstraction, mock provider (dev only), webhook pipeline,
// checkout orchestration, reconciliation worker. The active provider is selected via Payments:ActiveProvider.
builder.Services.Configure<PaymentSettings>(builder.Configuration.GetSection(PaymentSettings.SectionName));

// Mock provider is registered only in Development — PaymentStartupGuard fails fast if it ends up
// as the active provider in Production.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
}

// Rollypay (СБП) — registered unconditionally; only active when Payments:ActiveProvider="rollypay".
// It fails safe when unconfigured (no SigningSecret → webhooks 401; no ApiKey → outbound calls throw).
builder.Services.AddHttpClient(RollypayPaymentProvider.HttpClientName, (sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<PaymentSettings>>().Value.Rollypay;
    var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://rollypay.io" : settings.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IPaymentProvider, RollypayPaymentProvider>();

builder.Services.AddSingleton<PaymentProviderRegistry>();
builder.Services.AddScoped<PaymentCheckoutService>();
builder.Services.AddScoped<PaymentWebhookProcessor>();
builder.Services.AddHostedService<PaymentReconciliationWorker>();

// Hourly reasoning-capability enrichment: pulls supports_reasoning + thinking_options
// from the 9Router node catalog snapshot (cached by CollectorService) and backfills
// the billing model_prices catalog. No-op when nodes are unreachable or already in sync.
builder.Services.AddHostedService<ReasoningEnrichmentWorker>();

// Rate limiter for the webhook endpoint. Per-IP fixed window — providers typically retry on
// non-2xx, so we want a generous ceiling that still absorbs a misbehaving script.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PaymentWebhookEndpoint.RateLimitPolicy, httpContext =>
    {
        // Per-IP fixed window: 60 requests / minute. The mock self-webhook fires once per user
        // action, so this is well above any legitimate dev traffic.
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

var app = builder.Build();

// Must be the very first middleware so downstream pipeline (auth, OAuth redirect_uri
// generation, absolute URLs) sees the original scheme/host from Caddy.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

// TS-004: fail-fast in Production if the active payment provider is mock or unconfigured.
// In Development the mock is the expected active provider, so the guard is a no-op there.
PaymentStartupGuard.Validate(app.Services, app.Environment);

if (!app.Environment.IsDevelopment())
{
    app.UseLoggedExceptionHandler("/Error");
}

app.UseStaticFiles();

// TS-010: must run after UseStaticFiles and before UseAuthentication so that
// cookie/Accept-Language are honored for the auth pipeline and Razor views.
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();

// Site visit tracking. Registered only when the analytics sink is configured — the
// middleware resolves SiteVisitQueue/VisitorIdHasher per request and would throw on every
// page otherwise. Must sit after UseAuthentication so a signed-in visit carries the
// user's distinct_id.
if (app.Services.GetService<SiteVisitQueue>() is not null)
{
    app.UseMiddleware<SiteVisitTrackingMiddleware>();
}

// Rate limiter MUST be wired up before the webhook endpoint is mapped, so the policy is
// enforced on the request path. Other endpoints without a policy are unaffected.
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapRazorPages();
app.MapControllers();
app.MapOpenTelemetryEndpoints();

// TS-004: webhook endpoint — anonymous (providers can't authenticate as users), rate-limited,
// body-capped, raw-body verify-then-parse. See PaymentWebhookEndpoint for details.
PaymentWebhookEndpoint.Map(app);

// Grafana Alerting webhook → email via the Brevo HTTP path (SMTP is blocked on the prod box).
// Token-gated; disabled (503) unless Alerting:WebhookToken + Alerting:EmailTo are set.
AlertWebhookEndpoint.Map(app);

app.Run();

public partial class Program;
