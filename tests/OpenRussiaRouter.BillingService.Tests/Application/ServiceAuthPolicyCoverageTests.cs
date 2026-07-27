using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.BillingService.Infrastructure.Auth;

namespace OpenRussiaRouter.BillingService.Tests.Application;

/// <summary>
/// Catches the classic "added a money-printing RPC, forgot to add it to
/// <c>ServiceAuth:MethodPolicies</c>" mistake. The interceptor fails closed on a missing policy,
/// so without this guard test the mistake only surfaces at runtime as a confusing
/// <c>Unauthenticated</c> on a correctly-authenticated caller — exactly what happened with
/// <c>GrantStartBalance</c>/<c>GrantFirstTopUpBonus</c>/<c>SetPlatformSetting</c> before T-002.
/// </summary>
public sealed class ServiceAuthPolicyCoverageTests
{
    [Fact]
    public void EveryProtectedMethod_HasAPolicyInShippedAppSettings()
    {
        var options = LoadShippedServiceAuthOptions();

        var missing = ServiceAuthInterceptor.ProtectedMethods
            .Where(m => !options.MethodPolicies.TryGetValue(m, out var callers) || callers.Length == 0)
            .ToArray();

        missing.Should().BeEmpty(
            "every method in ServiceAuthInterceptor.ProtectedMethods needs a ServiceAuth:MethodPolicies entry");
    }

    [Fact]
    public void NoPolicyTargetsAnUnprotectedMethod()
    {
        var options = LoadShippedServiceAuthOptions();

        var stale = options.MethodPolicies.Keys
            .Where(k => !ServiceAuthInterceptor.ProtectedMethods.Contains(k))
            .ToArray();

        stale.Should().BeEmpty(
            "a policy for a method the interceptor does not protect is dead config and hides intent");
    }

    [Fact]
    public void ShippedAppSettings_DoesNotSeedAnyRealDefaultToken()
    {
        // T-003: the shipped base appsettings.json MUST keep both s2s tokens blank. Real secrets
        // come from env (ServiceAuth__Tokens__<caller>). A non-blank default here would silently
        // re-open the hole that ServiceAuthStartupGuard exists to prevent.
        var options = LoadShippedServiceAuthOptions();

        var nonBlankDefaults = options.Tokens
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToArray();

        nonBlankDefaults.Should().BeEmpty(
            "ServiceAuth:Tokens in appsettings.json must be blank; real secrets come from env");
    }

    private static ServiceAuthOptions LoadShippedServiceAuthOptions()
    {
        var appSettingsPath = Path.Combine(
            FindRepositoryRoot(), "src", "OpenRussiaRouter.BillingService", "appsettings.json");

        File.Exists(appSettingsPath).Should().BeTrue($"expected the shipped config at {appSettingsPath}");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath)
            .Build();

        var options = new ServiceAuthOptions();
        configuration.GetSection("ServiceAuth").Bind(options);
        return options;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("OpenRussiaRouter.sln").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
