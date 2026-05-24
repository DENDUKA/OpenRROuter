using System.Net;
using Grpc.Net.ClientFactory;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("webapp");

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    })
    .AddCookie("External", o => o.Cookie.Name = ".orr.ext")
    .AddGoogle(options =>
    {
        options.SignInScheme = "External";
        options.ClientId = builder.Configuration["Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
        options.CallbackPath = "/signin-google";
        options.Scope.Add("email");
    });

builder.Services.AddAuthorization();

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
});
builder.Services.AddScoped<AuthClient>();

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
});
builder.Services.AddScoped<BillingClient>();

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
});
builder.Services.AddScoped<ControlPlaneClient>();

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

app.Run();

public partial class Program;
