using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class ActivityResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("Pages/Workspace/Activity.cshtml")]
    public void ActivityRazorPage_ContainsStatRowAndCards(string relativePath)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenRussiaRouter.WebApp", relativePath);
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("class=\"act-stat-row\"");
        content.Should().Contain("class=\"act-stat-card\"");
        content.Should().Contain("class=\"act-stat-label\"");
        content.Should().Contain("class=\"act-stat-value\"");
    }

    [Fact]
    public async Task AppCss_ContainsResponsiveStatRowRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".act-stat-row {");
        css.Should().Contain("repeat(4, minmax(0, 1fr))");
        css.Should().Contain("repeat(2, minmax(0, 1fr))");
        css.Should().Contain("minmax(0, 1fr)");
        css.Should().Contain(".act-stat-card {");
        css.Should().Contain("min-width: 0;");
    }

    [Fact]
    public async Task AppMinCss_ContainsResponsiveStatRowRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.min.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".act-stat-row{display:grid;grid-template-columns:repeat(4,minmax(0,1fr))");
        css.Should().Contain(".act-stat-row{grid-template-columns:repeat(2,minmax(0,1fr))");
        css.Should().Contain(".act-stat-row{grid-template-columns:minmax(0,1fr)");
        css.Should().Contain("min-width:0");
    }
}
