using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.ContentService.Application.Extensions;
using OpenRussiaRouter.ContentService.Application.Options;
using OpenRussiaRouter.ContentService.Infrastructure.Auth;
using OpenRussiaRouter.ContentService.Infrastructure.Grpc;

namespace OpenRussiaRouter.ContentService.Infrastructure.Startup;

public static class Startup
{
    /// <summary>Tool images are capped at 1 MiB; 8 MiB leaves headroom for the framing overhead.</summary>
    private const int MaxGrpcMessageBytes = 8 * 1024 * 1024;

    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            // Guard the admin surface (UpsertTool, UploadToolImage, drafts) with s2s auth.
            options.Interceptors.Add<ServiceAuthInterceptor>();
            options.MaxReceiveMessageSize = MaxGrpcMessageBytes;
            options.MaxSendMessageSize = MaxGrpcMessageBytes;
        });
        services.AddControllers();
        services.AddHealthChecks();

        services.AddOptions<ContentLimitsOptions>()
            .Bind(configuration.GetSection("ContentLimits"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<ServiceAuthOptions>(configuration.GetSection("ServiceAuth"));

        var connectionString = configuration.GetConnectionString("ContentDb");
        services.AddContentInfrastructure(connectionString);
        services.AddContentApplicationServices();

        return services;
    }

    public static WebApplication ConfigurePipeline(WebApplication app)
    {
        app.UseLoggedExceptionHandler("/error");
        app.MapGrpcService<GrpcContentService>();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
