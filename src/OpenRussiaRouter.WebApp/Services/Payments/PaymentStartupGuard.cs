using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Fail-fast check that runs at startup. In Production an explicitly configured payment
/// provider MUST be real — the mock is dev-only and must never be reachable there.
/// </summary>
public static class PaymentStartupGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the active provider configuration is
    /// unsafe for the current environment. Should be called from <c>Program.cs</c> after DI is built.
    /// </summary>
    public static void Validate(IServiceProvider services, IWebHostEnvironment environment)
    {
        if (environment is null) throw new ArgumentNullException(nameof(environment));
        if (services is null) throw new ArgumentNullException(nameof(services));

        // The guard is meaningful only outside Development. In Development the mock is the
        // expected active provider.
        if (environment.IsDevelopment())
        {
            return;
        }

        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<PaymentSettings>>().Value;
        var registry = scope.ServiceProvider.GetService<PaymentProviderRegistry>();

        var active = settings.ActiveProvider;
        if (string.IsNullOrWhiteSpace(active))
        {
            // Payments are optional. An empty provider disables checkout without blocking the
            // rest of the production WebApp while no real provider is configured.
            return;
        }

        if (string.Equals(active, "mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Payments:ActiveProvider='mock' is forbidden in Production. " +
                "Configure a real payment provider before deploying.");
        }

        if (registry is not null && registry.TryGet(active!) is null)
        {
            throw new InvalidOperationException(
                $"Payments:ActiveProvider='{active}' is not registered. " +
                $"Known providers: {string.Join(", ", registry.RegisteredNames)}.");
        }
    }
}
