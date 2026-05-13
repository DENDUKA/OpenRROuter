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
                    Assert.Equal("001_first", first.Version);
                    Assert.Equal("select 1;", first.Sql);
                },
                second =>
                {
                    Assert.Equal("002_second", second.Version);
                    Assert.Equal("select 2;", second.Sql);
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

        Assert.Throws<DirectoryNotFoundException>(() => SqlMigrationLoader.LoadFromDirectory(missingDirectory));
    }
}
