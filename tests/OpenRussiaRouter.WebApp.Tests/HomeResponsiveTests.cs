using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class HomeResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public void IndexRazorPage_ContainsResponsiveHeroAndProofGridRules()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenRussiaRouter.WebApp", "Pages", "Index.cshtml");
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain(".home-hero {");
        content.Should().Contain(".promo-badges {");
        content.Should().Contain(".promo-badge {");
        content.Should().Contain("gap: var(--spacing-sm);");
        content.Should().Contain("margin-bottom: var(--spacing-md);");
        content.Should().Contain(".proof-grid {");
        content.Should().Contain(".proof-item {");
        content.Should().Contain("min-width: 0;");
        content.Should().Contain("@media (max-width: 1100px)");
    }

    [Fact]
    public async Task Get_HomePage_RendersHeroAndProofGrid()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("home-hero");
        html.Should().Contain("class=\"home-section proof-grid\"");
        html.Should().Contain("proof-item");
    }
}
