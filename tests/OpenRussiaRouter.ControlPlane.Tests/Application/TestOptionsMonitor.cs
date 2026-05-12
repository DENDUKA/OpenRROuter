using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.ControlPlane.Tests.Application;

internal sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue => value;

    public TOptions Get(string? name) => value;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;

}

internal static class TestOptionsMonitor
{
    public static TestOptionsMonitor<TOptions> Create<TOptions>(TOptions value)
    {
        return new TestOptionsMonitor<TOptions>(value);
    }
}
