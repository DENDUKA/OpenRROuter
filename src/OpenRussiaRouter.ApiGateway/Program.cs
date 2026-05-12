using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.ControlPlane;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.ApiGateway.Router;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Health;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using ControlGrpc = OpenRussiaRouter.Contracts.Grpc.ControlPlane.V1.RouterControlPlane;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpcClient<AuthGrpc.AuthServiceClient>(options =>
{
    var address = builder.Configuration["Services:Auth"] ?? "http://localhost:5101";
    options.Address = new Uri(address);
});
builder.Services.AddGrpcClient<BillingGrpc.BillingServiceClient>(options =>
{
    var address = builder.Configuration["Services:Billing"] ?? "http://localhost:5102";
    options.Address = new Uri(address);
});
builder.Services.AddGrpcClient<ControlGrpc.RouterControlPlaneClient>(options =>
{
    var address = builder.Configuration["Services:ControlPlane"] ?? "http://localhost:5103";
    options.Address = new Uri(address);
});
builder.Services.AddScoped<IApiKeyAuthenticator, GrpcApiKeyAuthenticator>();
builder.Services.AddScoped<IBillingGatewayClient, GrpcBillingGatewayClient>();
builder.Services.AddScoped<IControlPlaneGatewayClient, GrpcControlPlaneGatewayClient>();
builder.Services.AddSingleton<IRouterNodeTokenProvider, ConfigurationRouterNodeTokenProvider>();
builder.Services.AddHttpClient<IRouterProxyClient, HttpRouterProxyClient>();

var app = builder.Build();

app.MapGet("/healthz", () => ServiceHealth.Ok("api-gateway"));
app.MapGet("/ready", () => ServiceHealth.Ok("api-gateway"));

app.MapGet("/", () => Results.Json(new
{
    service = "OpenRussiaRouter API Gateway",
    publicApi = new[] { "/v1/models", "/v1/chat/completions" }
}));

app.MapGet("/v1/models", async (
    HttpRequest request,
    IApiKeyAuthenticator authenticator,
    IBillingGatewayClient billing,
    CancellationToken cancellationToken) =>
{
    var auth = await AuthenticateAsync(request, authenticator, cancellationToken);
    if (!auth.Succeeded)
    {
        return GatewayErrors.Authentication(
            request,
            auth.ErrorCode ?? ErrorCodes.InvalidApiKey,
            "Invalid or missing API key.");
    }

    var models = await billing.ListPublicModelsAsync(cancellationToken).ConfigureAwait(false);
    return Results.Json(new
    {
        @object = "list",
        data = models.Select(model => new
        {
            id = model.Id,
            @object = "model",
            owned_by = "openrussiarouter",
            display_name = model.DisplayName
        })
    });
});

app.MapPost("/v1/chat/completions", async (
    HttpRequest request,
    IApiKeyAuthenticator authenticator,
    IBillingGatewayClient billing,
    IControlPlaneGatewayClient controlPlane,
    IRouterProxyClient routerProxy,
    CancellationToken cancellationToken) =>
{
    var requestId = CorrelationIdReader.Get(request);
    var startedAtUtc = DateTimeOffset.UtcNow;
    var auth = await AuthenticateAsync(request, authenticator, cancellationToken);
    if (!auth.Succeeded)
    {
        return GatewayErrors.Authentication(
            request,
            auth.ErrorCode ?? ErrorCodes.InvalidApiKey,
            "Invalid or missing API key.");
    }

    ChatCompletionRequestBody body;
    try
    {
        body = await ChatCompletionRequestReader.ReadBodyAsync(request.Body, cancellationToken).ConfigureAwait(false);
    }
    catch
    {
        return GatewayErrors.Validation(request, ErrorCodes.ValidationFailed, "Request body must be valid JSON.");
    }

    var metadata = body.Metadata;
    if (string.IsNullOrWhiteSpace(metadata.Model))
    {
        return GatewayErrors.Validation(request, ErrorCodes.ValidationFailed, "Request field 'model' is required.");
    }

    if (metadata.Stream)
    {
        return GatewayErrors.ServiceUnavailable(
            request,
            ErrorCodes.RouterUnavailable,
            "Streaming proxying is not wired to 9Router yet.");
    }

    var billingCheck = await billing.CheckBalanceAndModelAsync(
        auth.UserId!,
        metadata.Model,
        cancellationToken).ConfigureAwait(false);
    if (!billingCheck.Allowed)
    {
        return billingCheck.ErrorCode switch
        {
            ErrorCodes.InsufficientBalance => GatewayErrors.Billing(
                request,
                ErrorCodes.InsufficientBalance,
                "Insufficient balance."),
            ErrorCodes.ModelUnavailable => GatewayErrors.Validation(
                request,
                ErrorCodes.ModelUnavailable,
                "Requested model is not available."),
            _ => GatewayErrors.ServiceUnavailable(
                request,
                billingCheck.ErrorCode ?? ErrorCodes.InternalError,
                "Billing service is unavailable.")
        };
    }

    var lease = await controlPlane.SelectRouterNodeAsync(metadata.Model, requestId, cancellationToken)
        .ConfigureAwait(false);
    if (lease is null)
    {
        return GatewayErrors.ServiceUnavailable(
            request,
            ErrorCodes.RouterUnavailable,
            "No healthy 9Router node is available.");
    }

    RouterProxyResponse routerResponse;
    try
    {
        routerResponse = await routerProxy.SendChatCompletionAsync(lease, body.Json, requestId, cancellationToken)
            .ConfigureAwait(false);
    }
    catch
    {
        await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, "failed", CancellationToken.None).ConfigureAwait(false);
        return GatewayErrors.ServiceUnavailable(
            request,
            ErrorCodes.RouterUnavailable,
            "9Router node request failed.");
    }

    var completedAtUtc = DateTimeOffset.UtcNow;
    var status = routerResponse.StatusCode is >= 200 and < 300 ? "succeeded" : "failed";

    if (status == "succeeded" && RouterUsageExtractor.TryExtract(routerResponse.Body, out var usage))
    {
        await billing.RecordUsageAsync(
            new GatewayUsageEvent(
                requestId,
                auth.UserId!,
                auth.ApiKeyId!,
                metadata.Model,
                lease.NodeId,
                usage,
                status,
                startedAtUtc,
                completedAtUtc),
            cancellationToken).ConfigureAwait(false);
    }

    await controlPlane.ReleaseRouterNodeAsync(lease.LeaseId, status, CancellationToken.None).ConfigureAwait(false);
    return Results.Content(routerResponse.Body, routerResponse.ContentType, statusCode: routerResponse.StatusCode);
});

app.Run();

static async Task<ApiKeyAuthenticationResult> AuthenticateAsync(
    HttpRequest request,
    IApiKeyAuthenticator authenticator,
    CancellationToken cancellationToken)
{
    if (!BearerTokenReader.TryRead(request, out var token))
    {
        return ApiKeyAuthenticationResult.Failed(ErrorCodes.AuthenticationRequired);
    }

    return await authenticator.AuthenticateAsync(token, cancellationToken).ConfigureAwait(false);
}
