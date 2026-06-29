using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenRussiaRouter.WebApp.Tests;

public class LegalPagesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public LegalPagesTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task PrivacyPage_ReturnsOk()
    {
        var response = await _client.GetAsync("/legal/privacy");
        response.EnsureSuccessStatusCode();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OfferPage_ReturnsOk()
    {
        var response = await _client.GetAsync("/legal/offer");
        response.EnsureSuccessStatusCode();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/legal/privacy", "ru", "Политика конфиденциальности")]
    [InlineData("/legal/privacy", "en", "Privacy Policy")]
    [InlineData("/legal/offer", "ru", "Публичная оферта")]
    [InlineData("/legal/offer", "en", "Public Offer")]
    public async Task LegalPage_ContainsLocalizedTitle(string path, string culture, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $".AspNetCore.Culture=c={culture}|uic={culture}");
        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(title, content);
    }

    [Fact]
    public async Task PrivacyPage_DoesNotContainWpgVpnTelegram()
    {
        var response = await _client.GetAsync("/legal/privacy");
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Web Point Gateway", content);
        Assert.DoesNotContain("WPG", content);
        Assert.DoesNotContain("VPN", content);
        Assert.DoesNotContain("Telegram", content);
    }

    [Fact]
    public async Task OfferPage_DoesNotContainWpgVpnTelegram()
    {
        var response = await _client.GetAsync("/legal/offer");
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Web Point Gateway", content);
        Assert.DoesNotContain("WPG", content);
        Assert.DoesNotContain("VPN", content);
        Assert.DoesNotContain("Telegram", content);
    }

    [Fact]
    public async Task PrivacyPage_ContainsLlmApiService()
    {
        var response = await _client.GetAsync("/legal/privacy");
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("LLM", content);
    }

    [Fact]
    public async Task OfferPage_ContainsLlmApiService()
    {
        var response = await _client.GetAsync("/legal/offer");
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("API", content);
    }

    [Theory]
    [InlineData("/legal/privacy", "Политика конфиденциальности")]
    [InlineData("/legal/offer", "Публичная оферта")]
    public async Task LegalPages_ContainDocumentHeadingMarkersAndFooter(string path, string heading)
    {
        var response = await _client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains($"<h1>{heading}", content);
        Assert.Contains("OpenRussiaRouter", content);
        Assert.DoesNotContain("{{ORR_SERVICE_NAME}}", content);
        Assert.Contains("{{ORR_PUBLIC_DOMAIN}}", content);
        Assert.Contains("href=\"/legal/privacy\"", content);
        Assert.Contains("href=\"/legal/offer\"", content);
    }

    [Theory]
    [InlineData("/signup")]
    [InlineData("/invite/test-token")]
    public async Task AccountCreationForms_RenderRequiredLegalConsent(string path)
    {
        var response = await _client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("AcceptLegal", content);
        Assert.Contains("required", content);
        Assert.Contains("target=\"_blank\"", content);
        Assert.Contains("rel=\"noopener noreferrer\"", content);
    }
}
