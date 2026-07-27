using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Infrastructure.Auth;

namespace OpenRussiaRouter.BillingService.Tests.Application;

/// <summary>
/// Behaviour tests for the fail-fast <see cref="ServiceAuthStartupGuard"/> (T-003). The guard is
/// the only thing keeping a non-Development runtime from shipping without enforceable s2s
/// secrets, since the shipped appsettings.json keeps both tokens blank.
/// </summary>
public sealed class ServiceAuthStartupGuardTests
{
    private const string Dev = "Development";
    private const string Prod = "Production";

    [Fact]
    public void Development_PassesEvenWhenTokensAreBlank()
    {
        var services = BuildServiceProvider(new ServiceAuthOptions());

        var act = () => ServiceAuthStartupGuard.Validate(services, Env(Dev));

        act.Should().NotThrow("Development is allowed to run on the seeded dev tokens or blank.");
    }

    [Fact]
    public void Production_ThrowsWhenApiGatewayTokenIsBlank()
    {
        var options = new ServiceAuthOptions
        {
            Tokens = { ["apigateway"] = "", ["webappadmin"] = "real-secret" },
            MethodPolicies = { ["RecordUsage"] = new[] { "apigateway" } }
        };
        var services = BuildServiceProvider(options);

        var act = () => ServiceAuthStartupGuard.Validate(services, Env(Prod));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*apigateway*ServiceAuth__Tokens__<caller>*");
    }

    [Fact]
    public void Production_ThrowsWhenWebappAdminTokenIsBlank()
    {
        var options = new ServiceAuthOptions
        {
            Tokens = { ["apigateway"] = "real-secret", ["webappadmin"] = "" }
        };
        var services = BuildServiceProvider(options);

        var act = () => ServiceAuthStartupGuard.Validate(services, Env(Prod));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*webappadmin*ServiceAuth__Tokens__<caller>*");
    }

    [Fact]
    public void Production_ThrowsWhenBothTokensAreBlank()
    {
        var options = new ServiceAuthOptions
        {
            Tokens = { ["apigateway"] = "", ["webappadmin"] = "   " }
        };
        var services = BuildServiceProvider(options);

        var act = () => ServiceAuthStartupGuard.Validate(services, Env(Prod));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*apigateway, webappadmin*");
    }

    [Fact]
    public void Production_PassesWhenBothTokensAreConfigured()
    {
        var options = new ServiceAuthOptions
        {
            Tokens = { ["apigateway"] = "real-gateway-secret", ["webappadmin"] = "real-admin-secret" }
        };
        var services = BuildServiceProvider(options);

        var act = () => ServiceAuthStartupGuard.Validate(services, Env(Prod));

        act.Should().NotThrow("both required caller tokens are non-blank, the interceptor can enforce.");
    }

    [Fact]
    public void Production_ThrowsWhenCallerMissingFromTokensDictionary()
    {
        // A completely absent entry must be treated the same way as a blank string.
        var options = new ServiceAuthOptions
        {
            Tokens = { ["apigateway"] = "real-secret" }
        };
        var services = BuildServiceProvider(options);

        var act = () => ServiceAuthStartupGuard.Validate(services, Env(Prod));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*webappadmin*");
    }

    [Fact]
    public void ThrowsOnNullArguments()
    {
        var services = BuildServiceProvider(new ServiceAuthOptions());

        ((Action)(() => ServiceAuthStartupGuard.Validate(services!, null!)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => ServiceAuthStartupGuard.Validate(null!, Env(Prod))))
            .Should().Throw<ArgumentNullException>();
    }

    private static IServiceProvider BuildServiceProvider(ServiceAuthOptions options)
    {
        var services = new ServiceCollection();
        // IOptions<ServiceAuthOptions> — singleton accessor for Validate
        services.AddSingleton(Options.Create(options));
        return services.BuildServiceProvider(validateScopes: false);
    }

    private static IWebHostEnvironment Env(string environmentName)
    {
        var env = new Moq.Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        return env.Object;
    }
}