using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.AuthService.Application;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Security;
using OpenRussiaRouter.BuildingBlocks.Health;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton<Sha256SecretHasher>();
builder.Services.AddSingleton<Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<InviteRegistrationService>();
builder.Services.AddSingleton<ApiKeyFactory>();
var authDbConnectionString = builder.Configuration.GetConnectionString("AuthDb");
if (!string.IsNullOrWhiteSpace(authDbConnectionString))
{
    builder.Services.AddSingleton<IAuthDbConnectionFactory>(_ => new NpgsqlAuthDbConnectionFactory(authDbConnectionString));
    builder.Services.AddSingleton<IAuthRepository, DapperAuthRepository>();
    builder.Services.AddSingleton<AuthAccountApplicationService>();
    builder.Services.AddSingleton<IAuthApiKeyValidator, DapperAuthApiKeyValidator>();
}
else
{
    builder.Services.AddSingleton<IAuthApiKeyValidator, InMemoryAuthApiKeyValidator>();
}

var app = builder.Build();

app.MapGrpcService<GrpcAuthService>();
app.MapGet("/healthz", () => ServiceHealth.Ok("auth-service"));
app.MapGet("/ready", () => ServiceHealth.Ok("auth-service"));

app.MapGet("/", () => Results.Json(new
{
    service = "OpenRussiaRouter Auth Service",
    contracts = new[] { "ValidateApiKey", "CreateInvite", "CompleteInvite", "CreateApiKey", "RevokeApiKey" }
}));

app.Run();
