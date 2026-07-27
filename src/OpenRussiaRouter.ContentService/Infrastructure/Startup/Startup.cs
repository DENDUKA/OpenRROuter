using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Grpc;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.ContentService.Application.Extensions;
using OpenRussiaRouter.ContentService.Application.Options;
using OpenRussiaRouter.ContentService.Infrastructure.Grpc;

namespace OpenRussiaRouter.ContentService.Infrastructure.Startup;

public static class Startup
{
    /// <summary>Tool images are capped at 1 MiB; 8 MiB leaves headroom for the framing overhead.</summary>
    private const int MaxGrpcMessageBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Code-owned allowlist of mutating ContentService gRPC methods. Every method listed here
    /// MUST also have a <c>ServiceAuth:MethodPolicies</c> entry in every environment.
    /// </summary>
    internal static readonly HashSet<string> ProtectedContentMethods = new(StringComparer.Ordinal)
    {
        "ListToolsForAdmin",
        "GetToolForAdmin",
        "UpsertTool",
        "DeleteTool",
        "SetToolPublished",
        "ListToolImages",
        "UploadToolImage",
        "DeleteToolImage",
        "UpsertToolCategory",
        "DeleteToolCategory"
    };

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
        services.Configure<ServiceAuthServerOptions>(o => o.ProtectedMethods = ProtectedContentMethods);

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
