using System.Text.Json;

namespace OpenRussiaRouter.E2ETests;

static class TestConfiguration
{
    private static readonly Lazy<LocalConfig?> _local = new(LoadLocal);

    public static string BaseUrl =>
        Env("ORR_E2E_BASE_URL") ?? _local.Value?.BaseUrl ?? "http://localhost:5004";

    public static string? UserEmail =>
        Env("ORR_E2E_USER_EMAIL") ?? _local.Value?.UserEmail;

    public static string? UserPassword =>
        Env("ORR_E2E_USER_PASSWORD") ?? _local.Value?.UserPassword;

    public static bool IsConfigured => UserEmail is not null && UserPassword is not null;

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static LocalConfig? LoadLocal()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "e2e.local.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LocalConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private sealed class LocalConfig
    {
        public string? BaseUrl { get; init; }
        public string? UserEmail { get; init; }
        public string? UserPassword { get; init; }
    }
}
