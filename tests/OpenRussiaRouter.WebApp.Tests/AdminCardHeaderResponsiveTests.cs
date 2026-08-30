using System.Net;
using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class AdminCardHeaderResponsiveTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("Pages/Admin/Models.cshtml")]
    [InlineData("Pages/Admin/ModelPrices.cshtml")]
    [InlineData("Pages/Admin/Providers.cshtml")]
    [InlineData("Pages/Admin/Tags.cshtml")]
    [InlineData("Pages/Admin/Tools.cshtml")]
    [InlineData("Pages/Admin/Nodes.cshtml")]
    [InlineData("Pages/Admin/NodeDetails.cshtml")]
    [InlineData("Pages/Admin/NodeEdit.cshtml")]
    [InlineData("Pages/Admin/NodeProviderLimits.cshtml")]
    [InlineData("Pages/Admin/Users.cshtml")]
    [InlineData("Pages/Admin/Broadcast.cshtml")]
    public void AdminRazorPage_SplitHeadersAndForms_PreserveCardHeaderStructure(string relativePath)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenRussiaRouter.WebApp", relativePath);
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("adm-card-header");
    }

    [Fact]
    public async Task AppCss_ContainsSplitCardHeaderAndBtnRowRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".adm-card-header--split {");
        css.Should().Contain(".adm-card-header--split > .adm-btn-row { margin-top: 0; }");
        css.Should().Contain(".adm-btn-row { display: flex; gap: var(--spacing-md); margin-top: var(--spacing-lg); }");
    }

    [Fact]
    public async Task AppMinCss_ContainsSplitCardHeaderAndBtnRowRules()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/app.min.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".adm-card-header--split{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:var(--spacing-md);}");
        css.Should().Contain(".adm-card-header--split>.adm-btn-row{margin-top:0;}");
        css.Should().Contain(".adm-btn-row{display:flex;gap:var(--spacing-md);margin-top:var(--spacing-lg);}");
    }
}
