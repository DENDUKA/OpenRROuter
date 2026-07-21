using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ContentService.Application.Abstractions.Persistence;
using OpenRussiaRouter.ContentService.Application.Services;
using OpenRussiaRouter.ContentService.Infrastructure.Persistence;
using OpenRussiaRouter.ContentService.Infrastructure.Repository;

namespace OpenRussiaRouter.ContentService.Application.Extensions;

public static class ContentExtensions
{
    public static IServiceCollection AddContentInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IContentDbConnectionFactory>(_ => new ContentDbConnectionFactory(connectionString));
            services.AddSingleton<IContentRepository, ContentRepository>();
        }
        else
        {
            services.AddSingleton<IContentRepository, InMemoryContentRepository>();
        }

        return services;
    }

    public static IServiceCollection AddContentApplicationServices(this IServiceCollection services)
    {
        services.TryAddSingletonTimeProvider();
        services.AddSingleton<ContentApplicationService>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
