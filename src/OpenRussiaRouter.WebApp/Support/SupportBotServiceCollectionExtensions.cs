using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Support;

public static class SupportBotServiceCollectionExtensions
{
    /// <summary>
    /// Wires the support Telegram-bot channel (BOT-001): settings, the bounded in-memory
    /// event queue, the page-facing notifier and the background delivery worker. Registered
    /// unconditionally — with the channel disabled (default) the notifier is a no-op and the
    /// worker idles on an empty queue, so the bot can never block WebApp startup or the
    /// /support form. <c>Support:Bot:AuthToken</c> is a secret: env/secret storage only.
    /// </summary>
    public static IServiceCollection AddSupportBotInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SupportBotSettings>(config.GetSection(SupportBotSettings.SectionName));

        // Singleton queue shared between request-scoped enqueuers (via the notifier) and the
        // singleton background worker.
        services.AddSingleton<SupportBotEventQueue>();
        services.AddSingleton<ISupportBotEventQueue>(sp => sp.GetRequiredService<SupportBotEventQueue>());
        services.AddSingleton<ISupportBotNotifier, SupportBotNotifier>();

        services.AddHttpClient(SupportBotEventWorker.HttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<SupportBotSettings>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
        });
        services.AddHostedService<SupportBotEventWorker>();

        return services;
    }
}
