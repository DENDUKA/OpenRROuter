using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Application.Extensions;

/// <summary>
/// Per-API-key (с фолбэком per-IP) fixed-window rate limiting для публичных chat-completions
/// эндпоинтов — единственной дорогой, трогающей баланс поверхности гейтвея. Защищает от
/// «убежавшего» клиента/скрипта до открытия API наружу. Настраивается секцией <c>RateLimiting</c>;
/// отказ отдаётся в OpenAI-формате ошибки (429 + <c>Retry-After</c>).
/// </summary>
public static class GatewayRateLimiting
{
    /// <summary>Имя политики, навешиваемой на chat-эндпоинты через <c>RequireRateLimiting</c>.</summary>
    public const string PolicyName = "gateway-chat";

    /// <summary>Имя политики для публичного эндпоинта /v1/models (мягче, чем chat).</summary>
    public const string ModelsPolicyName = "gateway-models";

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new GatewayRateLimitingOptions();
        configuration.GetSection("RateLimiting").Bind(options);

        var modelsOptions = new GatewayModelsRateLimitingOptions();
        configuration.GetSection("RateLimiting:Models").Bind(modelsOptions);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(PolicyName, httpContext =>
            {
                // Гейт через конфиг: при Enabled=false политика становится no-op (для локальной отладки).
                if (!options.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("__disabled__");
                }

                // Партиция на ключ (квота на каждый sk-or-*), фолбэк на IP для запросов без Bearer.
                var partitionKey = ResolvePartitionKey(httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            limiter.AddPolicy(ModelsPolicyName, httpContext =>
            {
                if (!options.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("__disabled__");
                }

                var partitionKey = ResolveModelsPartitionKey(httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = modelsOptions.PermitLimit,
                    Window = TimeSpan.FromSeconds(modelsOptions.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;

                // Считаем отказ до проверки HasStarted: лимит сработал в любом случае,
                // даже если тело ответа уже поехало и переписать его нельзя.
                LimitMetrics.RecordRejection(
                    LimitMetrics.LimitTypes.RateLimit,
                    BearerTokenReader.TryRead(http.Request, out _)
                        ? LimitMetrics.Subjects.ApiKey
                        : LimitMetrics.Subjects.Ip);

                if (http.Response.HasStarted)
                {
                    return;
                }

                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : options.WindowSeconds;
                http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                var requestId = CorrelationIdReader.Get(http.Request);
                await GatewayErrors.WriteAsync(
                    http,
                    "rate_limit_error",
                    ErrorCodes.RateLimited,
                    "Rate limit exceeded. Slow down and retry after the window resets.",
                    requestId);
            };
        });

        return services;
    }

    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        if (BearerTokenReader.TryRead(httpContext.Request, out var apiKey))
        {
            // Хешируем ключ: не держим секрет как ключ словаря партиций и ограничиваем длину.
            return "key:" + HashApiKey(apiKey);
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return "ip:" + ip;
    }

    private static string HashApiKey(string apiKey)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(apiKey), hash);
        return Convert.ToHexString(hash[..8]); // 16 hex-символов — достаточно против коллизий
    }

    private static string ResolveModelsPartitionKey(HttpContext httpContext)
    {
        // Для /v1/models партиция per-IP (эндпоинт анонимный, Bearer не требуется).
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return "models:ip:" + ip;
    }
}

/// <summary>Опции секции <c>RateLimiting</c> в appsettings.</summary>
public sealed class GatewayRateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Опции секции <c>RateLimiting:Models</c> для публичного /v1/models.</summary>
public sealed class GatewayModelsRateLimitingOptions
{
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
}
