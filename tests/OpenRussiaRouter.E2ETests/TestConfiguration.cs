using System.Text.Json;

namespace OpenRussiaRouter.E2ETests;

static class TestConfiguration
{
    private static readonly Lazy<LocalConfig?> _local = new(LoadLocal);

    public static string BaseUrl =>
        Env("RWT_E2E_BASE_URL") ?? _local.Value?.BaseUrl ?? "http://localhost:5004";

    public static string? UserEmail =>
        Env("RWT_E2E_USER_EMAIL") ?? _local.Value?.UserEmail;

    public static string? UserPassword =>
        Env("RWT_E2E_USER_PASSWORD") ?? _local.Value?.UserPassword;

    public static bool IsConfigured => UserEmail is not null && UserPassword is not null;

    /// <summary>
    /// Address (host or IP) the ControlPlane process can reach to connect to the E2E stub server.
    /// Defaults to "localhost" for local dev. In Docker/K8s set to "host.docker.internal" or
    /// the appropriate container network alias via RWT_E2E_STUB_ADDRESS.
    /// </summary>
    public static string StubAddress =>
        Env("RWT_E2E_STUB_ADDRESS") ?? _local.Value?.StubAddress ?? "localhost";

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
        public string? StubAddress { get; init; }
    }
}
