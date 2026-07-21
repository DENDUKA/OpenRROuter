using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.ContentService.Infrastructure.Auth;

namespace OpenRussiaRouter.ContentService.Tests.Application;

/// <summary>
/// Catches the classic "added an admin RPC, forgot its s2s policy" mistake. The interceptor fails
/// closed on a missing policy, so without this test the mistake only surfaces at runtime as a
/// confusing Unauthenticated on a correctly-authenticated caller.
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

    private static ServiceAuthOptions LoadShippedServiceAuthOptions()
    {
        var appSettingsPath = Path.Combine(
            FindRepositoryRoot(), "src", "OpenRussiaRouter.ContentService", "appsettings.json");

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
