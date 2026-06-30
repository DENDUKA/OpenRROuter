using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class ProductAnalyticsRegistrationTests
{
    [Fact]
    public void Resolve_EnabledFalse_ReturnsNoOp()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "false",
            ["ProductAnalytics:ProjectToken"] = "phc_some_token",
            ["ProductAnalytics:IdentitySalt"] = "any-salt",
            ["ProductAnalytics:HostUrl"] = "https://eu.i.posthog.com",
            ["ProductAnalytics:Environment"] = "production",
        });

        var analytics = services.GetRequiredService<IProductAnalytics>();

        analytics.Should().BeOfType<NoOpProductAnalytics>();
    }

    [Fact]
    public void Resolve_EmptyProjectToken_ReturnsNoOp()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:ProjectToken"] = string.Empty,
        });

        var analytics = services.GetRequiredService<IProductAnalytics>();

        analytics.Should().BeOfType<NoOpProductAnalytics>();
    }

    [Fact]
    public void Options_EnabledWithEmptyProjectToken_RemainsValidNoOpConfiguration()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:ProjectToken"] = string.Empty,
        });

        var act = () => services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ProductAnalyticsOptions>>()
            .Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void Resolve_NoProductAnalyticsSection_ReturnsNoOp()
    {
        var services = BuildServices(new Dictionary<string, string?>());

        var analytics = services.GetRequiredService<IProductAnalytics>();

        analytics.Should().BeOfType<NoOpProductAnalytics>();
    }

    [Fact]
    public void Resolve_NoOpAnalytics_DoesNotThrow()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "false",
        });

        var analytics = services.GetRequiredService<IProductAnalytics>();

        var act = () =>
        {
            analytics.UserVerified("user-1", "email");
            analytics.ApiKeyCreated("user-1");
            analytics.ApiRequestCompleted("user-1", new ApiRequestAnalytics(
                "gpt-4", 200, 10, 20, 100, "usage-1"));
            analytics.PaymentCompleted("user-1", new PaymentAnalytics(
                "yookassa", "RUB", 100m, "payment-1"));
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Options_BindFromConfiguration_ReflectsValues()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ProductAnalytics:Enabled"] = "true",
            ["ProductAnalytics:Provider"] = "PostHog",
            ["ProductAnalytics:HostUrl"] = "https://eu.i.posthog.com",
            ["ProductAnalytics:ProjectToken"] = "phc_token",
            ["ProductAnalytics:IdentitySalt"] = "my-salt",
            ["ProductAnalytics:Environment"] = "production",
        });

        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProductAnalyticsOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.Provider.Should().Be("PostHog");
        options.HostUrl.Should().Be("https://eu.i.posthog.com");
        options.ProjectToken.Should().Be("phc_token");
        options.IdentitySalt.Should().Be("my-salt");
        options.Environment.Should().Be("production");
    }

    [Fact]
    public void Options_DefaultValues_DisableAnalytics()
    {
        var services = BuildServices(new Dictionary<string, string?>());

        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProductAnalyticsOptions>>().Value;

        options.Enabled.Should().BeFalse();
        options.Provider.Should().Be("PostHog");
    }

    private static IServiceProvider BuildServices(IDictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddProductAnalytics(configuration);
        return services.BuildServiceProvider();
    }
}
