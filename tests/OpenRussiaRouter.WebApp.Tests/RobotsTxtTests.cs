using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenRussiaRouter.WebApp.Tests;

public class RobotsTxtTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RobotsTxtTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RobotsTxt_ReturnsOk()
    {
        var response = await _client.GetAsync("/robots.txt");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RobotsTxt_AllowsCrawlingAndDisallowsServiceSections()
    {
        var content = await _client.GetStringAsync("/robots.txt");

        Assert.Contains("User-agent: *", content);
        Assert.Contains("Allow: /", content);

        Assert.Contains("Disallow: /admin/", content);
        Assert.Contains("Disallow: /workspace/", content);
        Assert.Contains("Disallow: /webhook/", content);
        Assert.Contains("Disallow: /internal/", content);
        Assert.Contains("Disallow: /health", content);
        Assert.Contains("Disallow: /metrics", content);
    }

    [Fact]
    public async Task RobotsTxt_ReferencesAbsoluteSitemapUrl()
    {
        var content = await _client.GetStringAsync("/robots.txt");
        Assert.Contains("Sitemap: https://openrussiarouter.com/sitemap.xml", content);
    }
}
