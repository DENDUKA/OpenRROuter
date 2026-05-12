using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BillingService.Infrastructure.Repository;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Application.Extensions;

public static class BillingServiceExtensions
{

    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IBillingDbConnectionFactory>(_ => new BillingDbConnectionFactory(connectionString));
            services.AddSingleton<IBillingRepository, BillingRepository>();
        }
        else
        {
            services.AddSingleton<IBillingRepository, InMemoryBillingRepository>();
        }

        return services;
    }

    public static IServiceCollection AddBillingApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IUsageCostCalculator, UsageCostCalculator>();
        services.Configure<BillingPaginationOptions>(options =>
        {
            options.DefaultPageSize = 1000;
            options.MaxPageSize = 5000;
        });
        services.AddSingleton<BillingApplicationService>();

        return services;
    }
}