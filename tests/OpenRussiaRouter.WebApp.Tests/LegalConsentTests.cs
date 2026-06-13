using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Pages.Workspace;
using OpenRussiaRouter.WebApp.Services.Payments;
using System.Security.Claims;

namespace OpenRussiaRouter.WebApp.Tests;

public class LegalConsentTests : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;

    public LegalConsentTests(WebAppFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Signup_WithoutConsent_ShowsServerError()
    {
        var formData = new Dictionary<string, string>
        {
            { "Input.Email", "test@example.com" },
            { "Input.Password", "StrongPassword123!" },
            { "Input.ConfirmPassword", "StrongPassword123!" },
            { "Input.AcceptLegal", "false" }
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _client.PostAsync("/signup", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Contains("validation-message", responseBody);
        Assert.Contains("You must accept the Public Offer and Privacy Policy", responseBody);
    }

    [Fact]
    public async Task Invite_WithoutConsent_ShowsServerError()
    {
        var formData = new Dictionary<string, string>
        {
            { "Token", "test-token" },
            { "Input.Password", "StrongPassword123!" },
            { "Input.ConfirmPassword", "StrongPassword123!" },
            { "Input.AcceptLegal", "false" }
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _client.PostAsync("/invite/test-token", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Contains("You must accept the Public Offer and Privacy Policy", responseBody);
    }

    [Fact]
    public async Task LoginGoogle_WithoutConsent_ShowsServerError()
    {
        var formData = new Dictionary<string, string>
        {
            { "Input.Email", "test@example.com" },
            { "Input.Password", "password" },
            { "Input.AcceptLegal", "false" }
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _client.PostAsync("/login?handler=Google", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Contains("You must accept the Public Offer and Privacy Policy", responseBody);
    }

    [Fact]
    public async Task LoginPassword_DoesNotRequireConsent()
    {
        var formData = new Dictionary<string, string>
        {
            { "Input.Email", "test@example.com" },
            { "Input.Password", "password" }
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _client.PostAsync("/login", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("You must accept", responseBody);
    }

    [Fact]
    public async Task TopUp_WithoutConsent_DoesNotCreateCheckout()
    {
        var (model, billing) = CreateCreditsModel(acceptLegal: false);

        var result = await model.OnPostTopUpAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, billing.CreateCalls);
        Assert.True(model.ModelState.ContainsKey(nameof(CreditsModel.AcceptLegal)));
    }

    [Fact]
    public async Task TopUp_WithConsent_CreatesCheckout()
    {
        var (model, billing) = CreateCreditsModel(acceptLegal: true);

        var result = await model.OnPostTopUpAsync();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith("/pay/stub/", redirect.Url);
        Assert.Equal(1, billing.CreateCalls);
    }

    private static (CreditsModel Model, CheckoutFakeBilling Billing) CreateCreditsModel(bool acceptLegal)
    {
        var billing = new CheckoutFakeBilling();
        var provider = new StubProvider("mock");
        var registry = new PaymentProviderRegistry(
            [provider],
            Options.Create(new PaymentSettings { ActiveProvider = "mock" }));
        var checkout = new PaymentCheckoutService(
            billing,
            registry,
            NullLogger<PaymentCheckoutService>.Instance);

        var model = new CreditsModel(
            billing,
            authProvider: default!,
            currencyFormatter: default!,
            Options.Create(new PaymentSettings { ActiveProvider = "mock" }),
            checkout,
            NullLogger<CreditsModel>.Instance)
        {
            TopUpAmount = 10m,
            SelectedCurrency = "RUB",
            AcceptLegal = acceptLegal,
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                        "test"))
                }
            }
        };

        return (model, billing);
    }
}
