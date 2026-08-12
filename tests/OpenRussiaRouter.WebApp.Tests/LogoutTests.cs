using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenRussiaRouter.WebApp.Tests;

public class LogoutTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("/logout")]
    [InlineData("/en/logout")]
    public async Task Logout_ExpiresTheSharedAuthenticationCookieAtTheRootPath(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));

        var authenticationCookie = Assert.Single(setCookieHeaders,
            value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        Assert.Contains("path=/;", authenticationCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path=/en", authenticationCookie, StringComparison.OrdinalIgnoreCase);
    }
}
