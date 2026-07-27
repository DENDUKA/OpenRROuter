using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.BillingService.Infrastructure.Auth;

/// <summary>
/// Fail-fast check that runs at startup. Outside Development the s2s caller tokens
/// (<c>apigateway</c>, <c>webappadmin</c>) MUST be configured — without them the
/// <see cref="ServiceAuthInterceptor"/> has nothing to compare against and any caller that can
/// reach gRPC port 5002 can grant themselves balance through the mutating RPCs.
/// </summary>
/// <remarks>
/// The shipped <c>appsettings.json</c> deliberately keeps both tokens blank (T-003). A blank
/// token is only acceptable in <c>Development</c>, where the matching
/// <c>appsettings.Development.json</c> seeds dev values for <c>dotnet run</c> and unit tests.
/// In any other environment the operator MUST supply real secrets via
/// <c>ServiceAuth__Tokens__apigateway</c> / <c>ServiceAuth__Tokens__webappadmin</c>.
/// </remarks>
public static class ServiceAuthStartupGuard
{
    /// <summary>
    /// Caller ids that must carry a non-empty token outside Development. Matches the
    /// <c>webappadmin</c>/<c>apigateway</c> pair used by the shipped <c>MethodPolicies</c>.
    /// </summary>
    private static readonly string[] RequiredCallers = { "apigateway", "webappadmin" };

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any required caller token is missing or
    /// blank outside Development. Should be called from <c>Program.cs</c> after the host is built.
    /// </summary>
    public static void Validate(IServiceProvider services, IWebHostEnvironment environment)
    {
        if (environment is null) throw new ArgumentNullException(nameof(environment));
        if (services is null) throw new ArgumentNullException(nameof(services));

        // In Development the dev tokens come from appsettings.Development.json; the guard is
        // meaningful only outside it.
        if (environment.IsDevelopment())
        {
            return;
        }

        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ServiceAuthOptions>>().Value;

        var missing = new List<string>(RequiredCallers.Length);
        foreach (var caller in RequiredCallers)
        {
            var token = options.Tokens.TryGetValue(caller, out var raw) ? raw : null;
            if (string.IsNullOrWhiteSpace(token))
            {
                missing.Add(caller);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "ServiceAuth:Tokens for " + string.Join(", ", missing) +
                " is empty outside Development. Configure ServiceAuth__Tokens__<caller> " +
                "before deploying — without these secrets the s2s interceptor cannot enforce " +
                "authentication on the money-printing BillingService RPCs.");
        }
    }
}
