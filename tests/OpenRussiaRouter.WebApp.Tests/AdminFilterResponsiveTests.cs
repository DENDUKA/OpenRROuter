using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class AdminFilterResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task RequestsPage_RendersFilterControlsWithCorrectClasses()
    {
        using var authedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            }));

        using var client = authedFactory.CreateClient();

        using var response = await client.GetAsync("/admin/requests");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("class=\"d-flex gap-2 align-items-start flex-wrap mb-3\"");
        body.Should().Contain("class=\"btn btn-sm btn-outline-secondary ms-toggle\"");
        body.Should().Contain("class=\"btn btn-sm btn-primary\">Apply</button>");
        body.Should().Contain("class=\"btn btn-sm btn-info\" id=\"calculateBtn\">Calculate</button>");
        body.Should().Contain("class=\"form-control form-control-sm ms-search\"");
        body.Should().Contain("document.addEventListener('keydown', function(e)");
        body.Should().Contain("if (e.key === 'Escape')");
        body.Should().Contain("openPanels.forEach(function(panel)");
        body.Should().Contain("toggle.setAttribute('aria-expanded', 'false')");
        body.Should().Contain("toggle.focus()");
        body.Should().Contain("repositionPanel(panel)");
        body.Should().Contain("preventScroll: true");
    }

    [Fact]
    public async Task TopUpsPage_RendersFilterControlsWithCorrectClasses()
    {
        using var authedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            }));

        using var client = authedFactory.CreateClient();

        using var response = await client.GetAsync("/admin/topups");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("class=\"d-flex gap-2 align-items-end flex-wrap mb-3\"");
        body.Should().Contain("class=\"btn btn-sm btn-primary\">Apply</button>");
        body.Should().Contain("name=\"userQuery\"");
        body.Should().Contain("name=\"minAmount\"");
        body.Should().Contain("name=\"maxAmount\"");
    }

    [Fact]
    public async Task AppCss_ContainsRequiredUtilityAndButtonClasses()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".flex-wrap");
        css.Should().Contain(".align-items-start");
        css.Should().Contain(".align-items-end");
        css.Should().Contain(".btn-outline-secondary");
        css.Should().Contain(".btn-info");
        css.Should().Contain(".form-control-sm");
        css.Should().Contain("max-width: calc(100vw - 16px);");
        css.Should().Contain("max-height: min(260px, 45vh);");
    }

    [Fact]
    public async Task AppMinCss_ContainsRequiredDropdownViewportBoundRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.min.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain("max-width:calc(100vw - 16px)");
        css.Should().Contain("max-height:min(260px,45vh)");
    }
}
