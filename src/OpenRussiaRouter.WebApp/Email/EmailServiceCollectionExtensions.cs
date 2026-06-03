namespace OpenRussiaRouter.WebApp.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Wires the outbound-email stack: settings, the in-memory dispatch queue (shared
    /// singleton between request-scoped enqueuers and the background worker), the SMTP
    /// sender, and the broadcast service.
    /// </summary>
    public static IServiceCollection AddEmailInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EmailSettings>(config.GetSection("Email"));

        // The queue is a singleton so the worker (singleton) and request-scoped enqueuers
        // share it. The worker creates its own scope per message to resolve the scoped sender.
        services.AddSingleton<EmailDispatchQueue>();
        services.AddSingleton<IEmailDispatchQueue>(sp => sp.GetRequiredService<EmailDispatchQueue>());
        services.AddHostedService<EmailDispatchWorker>();

        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<EmailBroadcastService>();

        return services;
    }
}
