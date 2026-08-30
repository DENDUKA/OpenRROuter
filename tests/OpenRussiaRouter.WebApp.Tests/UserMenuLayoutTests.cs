using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class UserMenuLayoutTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task AuthenticatedPage_RendersUserMenuWithA11yAttributes()
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

        using var response = await client.GetAsync("/workspace/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("id=\"user-menu\"");
        body.Should().Contain("id=\"user-menu-trigger\"");
        body.Should().Contain("id=\"user-dropdown\"");
        body.Should().Contain("aria-haspopup=\"menu\"");
        body.Should().Contain("aria-expanded=\"false\"");
        body.Should().Contain("role=\"menu\"");
        body.Should().Contain("role=\"menuitem\"");
        body.Should().Contain("user-menu--open");
    }
}
