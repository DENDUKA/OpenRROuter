using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Extensions;

public static class AuthServiceExtensions
{

    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IAuthDbConnectionFactory>(_ => new AuthDbConnectionFactory(connectionString));
            services.AddSingleton<IAuthRepository, AuthRepository>();
            services.AddSingleton<IAuthApiKeyValidator, DapperAuthApiKeyValidator>();
        }
        else
        {
            services.AddSingleton<IAuthRepository, InMemoryAuthRepository>();
            services.AddSingleton<IAuthApiKeyValidator, DapperAuthApiKeyValidator>();
        }

        return services;
    }

    public static IServiceCollection AddAuthApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();

        // HMAC hasher for low-entropy codes (email verification, password reset).
        // Key from config; empty/missing key falls back to a dev-only default.
        var codeHmacKey = configuration["CodeHmacKey"];
        if (string.IsNullOrWhiteSpace(codeHmacKey))
        {
            codeHmacKey = "orr-dev-code-hmac-key-do-not-use-in-production";
        }

        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(codeHmacKey));
        services.AddSingleton(new HmacSecretHasher(keyBytes));

        services.AddSingleton<InviteRegistrationService>();
        services.AddSingleton<EmailVerificationService>();
        services.AddSingleton<ApiKeyFactory>();
        services.AddSingleton<LoginService>();
        services.AddSingleton<ExternalLoginService>();
        services.AddSingleton<AuthAccountApplicationService>();

        return services;
    }
}