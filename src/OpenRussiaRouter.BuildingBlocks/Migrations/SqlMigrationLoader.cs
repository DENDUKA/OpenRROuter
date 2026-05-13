namespace OpenRussiaRouter.BuildingBlocks.Migrations;

public static class SqlMigrationLoader
{
    public static IReadOnlyList<SqlMigration> LoadFromDirectory(string migrationsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);

        if (!Directory.Exists(migrationsDirectory))
        {
            throw new DirectoryNotFoundException($"SQL migrations directory was not found: {migrationsDirectory}");
        }

        return Directory.EnumerateFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new SqlMigration(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))
            .ToArray();
    }
}
