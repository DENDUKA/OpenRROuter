using System.Net;
using Grpc.Net.ClientFactory;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.Enrichment;
using OpenRussiaRouter.WebApp.Api;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Currency;
using OpenRussiaRouter.WebApp.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("webapp");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.WebApp");

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

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


builder.Services.AddAuthorization();

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

builder.Services.AddScoped<IOpenRouterCatalogClient, OpenRouterCatalogClient>();

builder.Services.AddEmailInfrastructure(builder.Configuration);

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
// as the active provider in Production. Real providers will register unconditionally when added.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
}

builder.Services.AddSingleton<PaymentProviderRegistry>();
builder.Services.AddScoped<PaymentCheckoutService>();
builder.Services.AddScoped<PaymentWebhookProcessor>();
builder.Services.AddHostedService<PaymentReconciliationWorker>();

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

// TS-004: fail-fast in Production if the active payment provider is mock or unconfigured.
// In Development the mock is the expected active provider, so the guard is a no-op there.
PaymentStartupGuard.Validate(app.Services, app.Environment);

if (!app.Environment.IsDevelopment())
{
    app.UseLoggedExceptionHandler("/Error");
}

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

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

app.Run();

public partial class Program;
