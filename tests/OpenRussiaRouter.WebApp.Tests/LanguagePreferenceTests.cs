using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// TS-010: covers the <c>POST /preferences/language</c> handler that flips the
/// UI culture via the <c>.AspNetCore.Culture</c> cookie. Anonymous, CSRF-protected,
/// and rejects external returnUrl.
/// </summary>
public class LanguagePreferenceTests : IClassFixture<WebAppFactory>
{
    private const string CultureCookie = ".AspNetCore.Culture";
    private readonly WebAppFactory _factory;
    private readonly HttpClient _client;

    public LanguagePreferenceTests(WebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>
    /// Sends a POST to <c>/preferences/language</c> with a valid antiforgery token
    /// scraped off a real GET (the home page renders the language switcher form
    /// which embeds the token). TS-010 spec keeps the handler
    /// <c>[ValidateAntiForgeryToken]</c>, so the tests go through the real
    /// antiforgery pipeline rather than disabling it.
    /// </summary>
    private async Task<HttpResponseMessage> PostLanguageAsync(Dictionary<string, string> form)
    {
        var bootstrap = await _client.GetAsync("/ru");
        bootstrap.EnsureSuccessStatusCode();
        var bootstrapBody = await bootstrap.Content.ReadAsStringAsync();

        // Locate the antiforgery form field + token on the language switcher form.
        // The hidden input is <input ... name="__RequestVerificationToken" value="...">.
        var match = System.Text.RegularExpressions.Regex.Match(
            bootstrapBody,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        // Forward the antiforgery cookie set by the GET response.
        var cookieHeader = bootstrap.Headers.TryGetValues("Set-Cookie", out var raw)
            ? string.Join("; ", raw!.Where(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)))
            : "";

        if (match.Success)
            form["__RequestVerificationToken"] = match.Groups[1].Value;

        var req = new HttpRequestMessage(HttpMethod.Post, "/preferences/language")
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (!string.IsNullOrEmpty(cookieHeader))
            req.Headers.Add("Cookie", cookieHeader);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task PostLanguage_ValidCulture_SetsCookie_AndRedirectsToLocalReturnUrl()
    {
        var resp = await PostLanguageAsync(new Dictionary<string, string>
        {
            { "culture", "en" },
            { "returnUrl", "/ru/models" },
        });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal(new Uri("/en/models", UriKind.Relative), resp.Headers.Location);

        // The response sets the .AspNetCore.Culture cookie with the new value.
        // Cookie values are URL-encoded, so look for the encoded form (c%3Den%7Cuic%3Den).
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));
        var cookieHeader = string.Join(",", setCookieHeaders!);
        Assert.Contains($"{CultureCookie}=c%3Den%7Cuic%3Den", cookieHeader);
    }

    [Fact]
    public async Task PostLanguage_SwitchingBackToRussian_UsesRuPrefix()
    {
        var resp = await PostLanguageAsync(new Dictionary<string, string>
        {
            { "culture", "ru" },
            { "returnUrl", "/en/models" },
        });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal(new Uri("/ru/models", UriKind.Relative), resp.Headers.Location);
    }

    [Fact]
    public async Task PostLanguage_PreservesQueryString_WhenSwitchingLocale()
    {
        var resp = await PostLanguageAsync(new Dictionary<string, string>
        {
            { "culture", "en" },
            { "returnUrl", "/ru/models?category=text" },
        });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal(new Uri("/en/models?category=text", UriKind.Relative), resp.Headers.Location);
    }

    [Fact]
    public async Task PostLanguage_ValidCulture_NoReturnUrl_RedirectsToHome()
    {
        var resp = await PostLanguageAsync(new Dictionary<string, string> { { "culture", "en" } });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal(new Uri("/en", UriKind.Relative), resp.Headers.Location);
    }

    [Fact]
    public async Task PostLanguage_InvalidCulture_DoesNotSetCookie_ButStillRedirects()
    {
        var resp = await PostLanguageAsync(new Dictionary<string, string>
        {
            { "culture", "klingon" },
            { "returnUrl", "/ru/models" },
        });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies!)
                Assert.DoesNotContain($"{CultureCookie}=", c);
        }
    }

    [Fact]
    public async Task PostLanguage_ExternalReturnUrl_FallsBackToHome()
    {
        var resp = await PostLanguageAsync(new Dictionary<string, string>
        {
            { "culture", "en" },
            { "returnUrl", "https://evil.example.com/phish" },
        });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location!.ToString();
        Assert.False(location.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
            $"External returnUrl must not be honored; got '{location}'.");
    }

    [Fact]
    public async Task PostLanguage_WorksAnonymously_NoRedirectToLogin()
    {
        // The handler is [AllowAnonymous] — a 302/3xx to /Login would mean the
        // [Authorize] attribute is leaking through.
        var resp = await PostLanguageAsync(new Dictionary<string, string> { { "culture", "en" } });

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString() ?? "";
        Assert.DoesNotContain("/Login", location, StringComparison.OrdinalIgnoreCase);
    }
}
