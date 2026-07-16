using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

public class PaymentStartupGuardTests
{
    private class TestEnv : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IServiceProvider BuildServiceProvider(string activeProvider, params IPaymentProvider[] providers)
        => BuildServiceProvider(new PaymentSettings { ActiveProvider = activeProvider }, providers);

    private static IServiceProvider BuildServiceProvider(PaymentSettings paymentSettings, params IPaymentProvider[] providers)
    {
        var settings = Options.Create(paymentSettings);
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        // Always register the registry — even with zero providers — so the guard can ask it
        // whether the active provider is known.
        services.AddSingleton<PaymentProviderRegistry>(_ =>
            new PaymentProviderRegistry(providers, settings));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Dev_environment_is_skipped()
    {
        var env = new TestEnv { EnvironmentName = "Development" };
        var sp = BuildServiceProvider(activeProvider: "mock");

        // No throw — guard is a no-op in Development.
        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().NotThrow();
    }

    [Fact]
    public void Production_with_empty_provider_disables_payments_and_passes()
    {
        var env = new TestEnv { EnvironmentName = "Production" };
        var sp = BuildServiceProvider(activeProvider: "");

        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().NotThrow();
    }

    [Fact]
    public void Production_with_mock_active_throws()
    {
        var env = new TestEnv { EnvironmentName = "Production" };
        var sp = BuildServiceProvider(activeProvider: "mock", new StubProvider("mock"));

        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*forbidden in Production*");
    }

    [Fact]
    public void Production_with_unknown_provider_throws()
    {
        var env = new TestEnv { EnvironmentName = "Production" };
        // Pass an empty providers list so the registry knows about no provider — the active
        // setting "rollypay" therefore resolves to nothing.
        var sp = BuildServiceProvider(activeProvider: "rollypay", Array.Empty<IPaymentProvider>());

        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public void Production_with_real_registered_provider_passes()
    {
        var env = new TestEnv { EnvironmentName = "Production" };
        var sp = BuildServiceProvider(activeProvider: "rollypay", new StubProvider("rollypay"));

        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().NotThrow();
    }

    [Fact]
    public void Production_with_rollypay_test_mode_throws()
    {
        var env = new TestEnv { EnvironmentName = "Production" };
        var sp = BuildServiceProvider(
            new PaymentSettings
            {
                ActiveProvider = "rollypay",
                Rollypay = new RollypaySettings { TestMode = true },
            },
            new StubProvider("rollypay"));

        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestMode*");
    }

    [Fact]
    public void Dev_environment_allows_rollypay_test_mode()
    {
        var env = new TestEnv { EnvironmentName = "Development" };
        var sp = BuildServiceProvider(
            new PaymentSettings
            {
                ActiveProvider = "rollypay",
                Rollypay = new RollypaySettings { TestMode = true },
            },
            new StubProvider("rollypay"));

        var act = () => PaymentStartupGuard.Validate(sp, env);
        act.Should().NotThrow();
    }
}
