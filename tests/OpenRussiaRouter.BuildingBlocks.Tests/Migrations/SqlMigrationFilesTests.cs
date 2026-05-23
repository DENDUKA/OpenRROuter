using FluentAssertions;

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

        sql.Should().ContainEquivalentOf($"create schema if not exists {service}");
        sql.Should().ContainEquivalentOf(firstTable);
        sql.Should().ContainEquivalentOf(secondTable);
        sql.Should().NotContainEquivalentOf("entity framework");
        sql.Should().NotContainEquivalentOf("dbcontext");
    }

    [Fact]
    public void AuthSeedAdminMigration_ExistsAndDoesNotStoreRawPassword()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", "auth", "002_seed_default_admin.sql"));

        sql.Should().Contain("admin@example.com");
        sql.Should().Contain("pbkdf2-sha256.210000.");
        sql.Should().NotContain("StrongPassword123!");
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
