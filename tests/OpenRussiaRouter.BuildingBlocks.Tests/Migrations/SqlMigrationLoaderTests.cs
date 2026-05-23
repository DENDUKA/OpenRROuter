using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Migrations;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Migrations;

public sealed class SqlMigrationLoaderTests
{
    [Fact]
    public void LoadFromDirectory_ReturnsSqlFilesOrderedByVersion()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "002_second.sql"), "select 2;");
            File.WriteAllText(Path.Combine(directory.FullName, "001_first.sql"), "select 1;");
            File.WriteAllText(Path.Combine(directory.FullName, "README.md"), "ignore");

            var migrations = SqlMigrationLoader.LoadFromDirectory(directory.FullName);

            Assert.Collection(
                migrations,
                first =>
                {
                    first.Version.Should().Be("001_first");
                    first.Sql.Should().Be("select 1;");
                },
                second =>
                {
                    second.Version.Should().Be("002_second");
                    second.Sql.Should().Be("select 2;");
                });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_MissingDirectory_Throws()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        ((Action)(() => SqlMigrationLoader.LoadFromDirectory(missingDirectory))).Should().Throw<DirectoryNotFoundException>();
    }
}
