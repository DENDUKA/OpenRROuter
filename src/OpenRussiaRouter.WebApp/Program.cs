using Grpc.Net.ClientFactory;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;
using OpenRussiaRouter.WebApp.Components;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddServerSideBlazor();

builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<ISessionStorage, SessionStorage>();

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
