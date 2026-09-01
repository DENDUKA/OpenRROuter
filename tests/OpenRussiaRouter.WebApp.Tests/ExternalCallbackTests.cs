using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages.Login;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class ExternalCallbackTests
{
    [Fact]
    public async Task OnGetAsync_NewUser_GrantsStartBalanceWhenEnabled()
    {
        var services = new ServiceCollection();
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultSignInScheme = "Cookies";
            options.DefaultAuthenticateScheme = "Cookies";
        }).AddCookie("Cookies");
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var externalIdentity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "google-sub-123"),
            new Claim(ClaimTypes.Email, "newuser@example.com")
        ], "External");
        var externalPrincipal = new ClaimsPrincipal(externalIdentity);
        var authProps = new AuthenticationProperties();
        authProps.Items["provider"] = "google";

        var authResult = AuthenticateResult.Success(new AuthenticationTicket(externalPrincipal, authProps, "External"));

        var authServiceMock = new FakeAuthenticationService(authResult);
        services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(authServiceMock);
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultSignInScheme = "Cookies";
        }).AddCookie("Cookies");
        httpContext.RequestServices = services.BuildServiceProvider();

        var authProvider = new FakeAuthProvider(new OpenRussiaRouter.WebApp.Services.Auth.LoginResponse(
            "orr_session_123",
            "user-new-1",
            "User",
            "newuser@example.com",
            IsNewUser: true));

        var billingProvider = new FakeBillingProvider();
        var model = new ExternalCallbackModel(authProvider, billingProvider, NullLogger<ExternalCallbackModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataScope(httpContext).Create()
        };

        var result = await model.OnGetAsync();

        result.Should().BeOfType<RedirectToPageResult>();
        ((RedirectToPageResult)result).PageName.Should().Be("/Workspace/ApiKeys");

        billingProvider.GrantedStartBalances.Should().ContainSingle(g => g.UserId == "user-new-1" && g.AmountUsd == 1.00m);
    }

    [Fact]
    public async Task OnGetAsync_ExistingUser_DoesNotGrantStartBalance()
    {
        var externalIdentity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "google-sub-existing"),
            new Claim(ClaimTypes.Email, "existing@example.com")
        ], "External");
        var externalPrincipal = new ClaimsPrincipal(externalIdentity);
        var authProps = new AuthenticationProperties();
        authProps.Items["provider"] = "google";

        var authResult = AuthenticateResult.Success(new AuthenticationTicket(externalPrincipal, authProps, "External"));

        var authServiceMock = new FakeAuthenticationService(authResult);
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(authServiceMock);
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultSignInScheme = "Cookies";
        }).AddCookie("Cookies");

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        var authProvider = new FakeAuthProvider(new OpenRussiaRouter.WebApp.Services.Auth.LoginResponse(
            "orr_session_456",
            "user-existing-1",
            "User",
            "existing@example.com",
            IsNewUser: false));

        var billingProvider = new FakeBillingProvider();
        var model = new ExternalCallbackModel(authProvider, billingProvider, NullLogger<ExternalCallbackModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataScope(httpContext).Create()
        };

        var result = await model.OnGetAsync();

        result.Should().BeOfType<RedirectToPageResult>();
        ((RedirectToPageResult)result).PageName.Should().Be("/Workspace/ApiKeys");

        billingProvider.GrantedStartBalances.Should().BeEmpty();
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private readonly AuthenticateResult _externalResult;

        public FakeAuthenticationService(AuthenticateResult externalResult)
        {
            _externalResult = externalResult;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (scheme == "External")
                return Task.FromResult(_externalResult);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }

    private sealed class FakeAuthProvider : AuthProvider
    {
        private readonly OpenRussiaRouter.WebApp.Services.Auth.LoginResponse _response;

        public FakeAuthProvider(OpenRussiaRouter.WebApp.Services.Auth.LoginResponse response) : base(null!)
        {
            _response = response;
        }

        public override Task<OpenRussiaRouter.WebApp.Services.Auth.LoginResponse?> LoginOrRegisterWithExternalProviderAsync(
            string provider, string providerSub, string email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OpenRussiaRouter.WebApp.Services.Auth.LoginResponse?>(_response);
        }
    }

    private sealed class FakeBillingProvider : BillingProvider
    {
        public List<(string UserId, decimal AmountUsd)> GrantedStartBalances { get; } = [];

        public FakeBillingProvider() : base(null!) { }

        public override Task<string?> GetPlatformSettingAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key == PlatformSettingKeys.PromotionsStartBalanceEnabled)
                return Task.FromResult<string?>("true");
            if (key == PlatformSettingKeys.PromotionsStartBalanceAmountUsd)
                return Task.FromResult<string?>("1.00");
            return Task.FromResult<string?>(null);
        }

        public override Task GrantStartBalanceAsync(string userId, decimal amountUsd, CancellationToken cancellationToken = default)
        {
            GrantedStartBalances.Add((userId, amountUsd));
            return Task.CompletedTask;
        }
    }
}
