using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class OAuthLocaleTests
{
    [Fact]
    public void Login_OnPostGoogle_IncludesCurrentCultureInProperties()
    {
        var prevCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            var config = new ConfigurationBuilder().Build();
            var authProvider = new AuthProvider(null!);
            var model = new LoginModel(authProvider, new NullStringLocalizer<SharedResource>(), NullLogger<LoginModel>.Instance, config)
            {
                PageContext = new PageContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var result = model.OnPostGoogle();

            result.Should().BeOfType<ChallengeResult>();
            var challenge = (ChallengeResult)result;
            challenge.AuthenticationSchemes.Should().Contain(GoogleDefaults.AuthenticationScheme);
            challenge.Properties.Should().NotBeNull();
            challenge.Properties!.Items.Should().ContainKey("provider").WhoseValue.Should().Be("google");
            challenge.Properties.Items.Should().ContainKey("culture").WhoseValue.Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevCulture;
        }
    }

    [Fact]
    public void Signup_OnPostYandex_IncludesCurrentCultureInProperties()
    {
        var prevCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            var config = new ConfigurationBuilder().Build();
            var authProvider = new AuthProvider(null!);
            var billingProvider = new BillingProvider(null!);
            var emailQueue = new EmailDispatchQueue();
            var model = new SignupModel(
                authProvider,
                billingProvider,
                emailQueue,
                new NullStringLocalizer<SharedResource>(),
                NullLogger<SignupModel>.Instance,
                config)
            {
                PageContext = new PageContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var result = model.OnPostYandex();

            result.Should().BeOfType<ChallengeResult>();
            var challenge = (ChallengeResult)result;
            challenge.AuthenticationSchemes.Should().Contain(AspNet.Security.OAuth.Yandex.YandexAuthenticationDefaults.AuthenticationScheme);
            challenge.Properties.Should().NotBeNull();
            challenge.Properties!.Items.Should().ContainKey("provider").WhoseValue.Should().Be("yandex");
            challenge.Properties.Items.Should().ContainKey("culture").WhoseValue.Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevCulture;
        }
    }
}
