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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

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
.AddInterceptor(() => new GrpcDeadlineInterceptor(grpcCallDeadline));
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseLoggedExceptionHandler("/Error");
}

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapRazorPages();
app.MapControllers();
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
