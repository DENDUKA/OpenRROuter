using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class TableScrollResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("Pages/Admin/Users.cshtml")]
    [InlineData("Pages/Admin/TopUps.cshtml")]
    [InlineData("Pages/Admin/Nodes.cshtml")]
    [InlineData("Pages/Admin/Models.cshtml")]
    [InlineData("Pages/Admin/Requests.cshtml")]
    [InlineData("Pages/Admin/Providers.cshtml")]
    [InlineData("Pages/Admin/NodeDetails.cshtml")]
    [InlineData("Pages/Workspace/Credits.cshtml")]
    public void RazorPages_WrapTablesInTableScroll(string relativePath)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenRussiaRouter.WebApp", relativePath);
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("class=\"table-scroll\"");
    }

    [Fact]
    public async Task AppCss_ContainsTableScrollAndAdminModelTableRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".table-scroll { overflow-x: auto; }");
    }
}

