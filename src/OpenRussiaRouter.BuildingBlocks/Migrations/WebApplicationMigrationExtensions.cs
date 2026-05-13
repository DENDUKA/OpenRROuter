using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace OpenRussiaRouter.BuildingBlocks.Migrations;

public static class WebApplicationMigrationExtensions
{
    public static async Task<WebApplication> ApplySqlMigrationsAsync(
        this WebApplication app,
        string connectionStringName,
        string service,
        string migrationsDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        if (!app.Configuration.GetValue("SqlMigrations:Enabled", true))
        {
            return app;
        }

        var connectionString = app.Configuration.GetConnectionString(connectionStringName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var migrations = SqlMigrationLoader.LoadFromDirectory(migrationsDirectory);
        var runner = new PostgresSqlMigrationRunner(connectionString);
        await runner.ApplyAsync(service, migrations, cancellationToken).ConfigureAwait(false);

        return app;
    }
}
