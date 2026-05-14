namespace OpenRussiaRouter.BuildingBlocks.Migrations;

public static class RepositoryMigrationPath
{
    public static string ForService(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var appRelativePath = Path.Combine(AppContext.BaseDirectory, "db", "migrations", service);
        if (Directory.Exists(appRelativePath))
        {
            return appRelativePath;
        }

        return Path.Combine(FindRepositoryRoot(), "db", "migrations", service);
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
