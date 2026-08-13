using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Tests;

public class SupportModelTests
{
    [Fact]
    public async Task OnPostAsync_valid_submission_enqueues_one_email_to_inbox_with_reply_to()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@rwt.com");
        model.Input = new SupportModel.InputModel
        {
            Email = "user@example.com",
            Subject = "Help please",
            Message = "Something is broken.",
        };

        await model.OnPostAsync();

        model.Sent.Should().BeTrue();
        queue.Reader.TryRead(out var msg).Should().BeTrue();
        msg!.To.Should().Be("support@rwt.com");
        msg.ReplyTo.Should().Be("user@example.com");
        msg.TextBody.Should().Contain("Something is broken.");
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task OnPostAsync_honeypot_filled_drops_submission_silently()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@rwt.com");
        model.Input = new SupportModel.InputModel
        {
            Email = "bot@spam.com",
            Subject = "Buy now",
            Message = "spam",
            Website = "http://spam.example", // honeypot — humans never fill this
        };

        await model.OnPostAsync();

        model.Sent.Should().BeTrue();                       // same success screen, no signal to the bot
        queue.Reader.TryRead(out _).Should().BeFalse();     // nothing delivered
    }

    [Fact]
    public async Task OnPostAsync_without_configured_inbox_does_not_enqueue_but_still_confirms()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: null);
        model.Input = new SupportModel.InputModel
        {
            Email = "user@example.com",
            Subject = "Hi",
            Message = "Question.",
        };

        await model.OnPostAsync();

        model.Sent.Should().BeTrue();                       // never dead-end the user over a misconfig
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void OnGet_prefills_email_for_signed_in_user()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@rwt.com", signedInEmail: "me@example.com");

        model.OnGet();

        model.Input.Email.Should().Be("me@example.com");
    }

    private static SupportModel CreateModel(
        EmailDispatchQueue queue, string? inbox, string? signedInEmail = null)
    {
        var user = signedInEmail is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim(ClaimTypes.Email, signedInEmail),
                    new Claim(ClaimTypes.Role, "User"),
                ],
                "test"));

        return new SupportModel(
            queue,
            Options.Create(new SupportSettings { Inbox = inbox }),
            NullStringLocalizer<SharedResource>.Instance,
            NullLogger<SupportModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }
}
