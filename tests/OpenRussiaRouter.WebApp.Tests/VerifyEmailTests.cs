using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Tests;

public class VerifyEmailTests
{
    private sealed class FakeAuthProvider : AuthProvider
    {
        private readonly LoginResponse _response;

        public FakeAuthProvider(LoginResponse response) : base(null!)
        {
            _response = response;
        }

        public override Task<LoginResponse> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class RecordingBotNotifier : ISupportBotNotifier
    {
        public List<string> RegisteredEmails { get; } = [];
        public void NotifySupportTicket(string email, string subject, string message) { }
        public void NotifyUserRegistered(string email) => RegisteredEmails.Add(email);
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task OnPostAsync_SuccessfulVerification_NotifiesSupportBotOfRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
        }).AddCookie("Cookies");
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var authProvider = new FakeAuthProvider(new LoginResponse(
            "session-1", "user-1", "User", "verified@example.com", false));
        var billingProvider = new BillingProvider(null!);
        var emailQueue = new EmailDispatchQueue();
        var botNotifier = new RecordingBotNotifier();

        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new PageActionDescriptor(), modelState);
        var model = new VerifyEmailModel(
            authProvider,
            billingProvider,
            emailQueue,
            NullStringLocalizer<SharedResource>.Instance,
            NullLogger<VerifyEmailModel>.Instance,
            botNotifier)
        {
            Email = "verified@example.com",
            Code = "123456",
            PageContext = new PageContext(actionContext),
            TempData = new TempDataScope(httpContext).Create()
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>();
        botNotifier.RegisteredEmails.Should().ContainSingle("verified@example.com");
    }
}
