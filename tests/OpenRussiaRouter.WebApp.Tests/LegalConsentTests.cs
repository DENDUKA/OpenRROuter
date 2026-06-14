using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Pages.Workspace;
using OpenRussiaRouter.WebApp;
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

        // TS-010: validation messages are localized. The default UI culture is `ru`
        // and the accept-legal message is one of the keys we deliberately keep
        // identical in both .resx files (the DataAnnotations lookup path returns the
        // resource key when no static accessor matches it; the model layer also adds
        // a ModelState entry with the same key). We assert that *some* validation
        // message was rendered rather than scraping language-specific text.
        Assert.Contains("validation-message", responseBody);
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

        Assert.Contains("validation-message", responseBody);
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

        // TS-010: the AcceptLegal message is locale-specific; assert the request did
        // not bounce through the Google handler (which is the one gated by AcceptLegal).
        Assert.NotEqual(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Input.Password", responseBody);
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
        model.TopUpAmountRaw = "10.50";

        var result = await model.OnPostTopUpAsync();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith("/pay/stub/", redirect.Url);
        Assert.Equal(1, billing.CreateCalls);
        Assert.Equal(10.50m, billing.LastCreate!.Value.displayAmount);
    }

    [Fact]
    public async Task TopUp_WithInvalidInvariantAmount_DoesNotCreateCheckout()
    {
        var (model, billing) = CreateCreditsModel(acceptLegal: true);
        model.TopUpAmountRaw = "not-an-amount";

        var result = await model.OnPostTopUpAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, billing.CreateCalls);
        Assert.Equal("Credits_AmountInvalid", model.ErrorMessage);
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
            NullStringLocalizer<SharedResource>.Instance,
            NullLogger<CreditsModel>.Instance)
        {
            TopUpAmountRaw = "10.00",
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
