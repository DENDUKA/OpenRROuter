namespace OpenRussiaRouter.BuildingBlocks.Tests.Migrations;

public sealed class SqlMigrationFilesTests
{
    [Theory]
    [InlineData("auth", "auth.users", "auth.api_keys")]
    [InlineData("billing", "billing.ledger_entries", "billing.usage_records")]
    [InlineData("control", "control.router_nodes", "control.router_leases")]
    public void InitialMigration_ExistsAndUsesPlainSql(string service, string firstTable, string secondTable)
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", service, "001_initial.sql"));

        Assert.Contains($"create schema if not exists {service}", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstTable, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secondTable, sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entity framework", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbcontext", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenRussiaRouter.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
