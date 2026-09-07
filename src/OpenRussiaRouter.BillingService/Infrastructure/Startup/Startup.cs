using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Application.Extensions;
using OpenRussiaRouter.BillingService.Infrastructure.Grpc;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Infrastructure.Workers;
using OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.BillingService.Infrastructure.Startup;

public static class Startup
{
    /// <summary>
    /// Code-owned allowlist of mutating BillingService gRPC methods. Every method listed here
    /// MUST also have a <c>ServiceAuth:MethodPolicies</c> entry in every environment.
    /// </summary>
    internal static readonly HashSet<string> ProtectedBillingMethods = new(StringComparer.Ordinal)
    {
        "RecordUsage",
        "CreateVirtualTopUp",
        "UpsertApiKeyLimitSettings",
        "UpsertModelPrice",
        "UpsertModels",
        "UpsertTag",
        "DeleteTag",
        "CreatePayment",
        "AttachProviderRef",
        "ConfirmPaymentByProvider",
        "ApplyPaymentEvent",
        "AcquireStalePayments",
        "UpsertModelProvider",
        "DeleteModelProvider",
        // Money-printing / settings RPCs (T-002): these write positive ledger entries or alter
        // operational settings and must never be reachable without s2s auth.
        "GrantStartBalance",
        "GrantFirstTopUpBonus",
        "SetPlatformSetting"
    };

    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            // TS-003: guard every mutating BillingService gRPC method with s2s auth.
            options.Interceptors.Add<ServiceAuthInterceptor>();
        });
        services.AddControllers();
        services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("BillingDb");
        services.AddBillingInfrastructure(connectionString);
        services.AddBillingApplicationServices();
        services.AddProductAnalytics(configuration);
        services.AddOptions<YandexMetrikaOfflineConversionOptions>()
            .Bind(configuration.GetSection(YandexMetrikaOfflineConversionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<YandexMetrikaOfflineConversionOptions>,
            YandexMetrikaOfflineConversionOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<IYandexMetrikaClient, YandexMetrikaClient>((serviceProvider, client) =>
        {
            var metrikaOptions = serviceProvider
                .GetRequiredService<IOptions<YandexMetrikaOfflineConversionOptions>>().Value;
            client.BaseAddress = new Uri(metrikaOptions.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = metrikaOptions.HttpTimeout;
        });
        services.Configure<ExchangeRateOptions>(configuration.GetSection("ExchangeRates"));
        services.Configure<PaymentOptions>(configuration.GetSection("Payments"));
        services.Configure<ServiceAuthOptions>(configuration.GetSection("ServiceAuth"));
        services.Configure<ServiceAuthServerOptions>(o => o.ProtectedMethods = ProtectedBillingMethods);
        services.Configure<OpenRussiaRouter.BillingService.Infrastructure.Telegram.TelegramBotOptions>(
            configuration.GetSection(OpenRussiaRouter.BillingService.Infrastructure.Telegram.TelegramBotOptions.SectionName));
        services.AddHttpClient("TelegramBot");
        var authUrl = configuration["GrpcServices:Auth"];
        if (!string.IsNullOrWhiteSpace(authUrl))
        {
            services.AddGrpcClient<Contracts.Grpc.Auth.V1.AuthService.AuthServiceClient>(o => o.Address = new Uri(authUrl));
        }
        services.AddSingleton<OpenRussiaRouter.BillingService.Infrastructure.Telegram.ITelegramBotNotifier,
            OpenRussiaRouter.BillingService.Infrastructure.Telegram.TelegramBotNotifier>();
        services.AddHttpClient("BillingFx");
        services.AddSingleton<FxQuoteService>();
        services.AddHostedService<FxRateRefresher>();
        services.AddHostedService<PaymentEventRecoveryWorker>();
        services.AddHostedService<YandexMetrikaOfflineConversionWorker>();

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcBillingService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
