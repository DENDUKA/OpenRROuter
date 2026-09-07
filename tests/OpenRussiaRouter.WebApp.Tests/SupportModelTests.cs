using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services.Analytics;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Tests;

public class SupportModelTests
{
    [Fact]
    public async Task OnPostAsync_valid_submission_enqueues_one_email_to_inbox_with_reply_to()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@orr.com");
        model.Input = new SupportModel.InputModel
        {
            Email = "user@example.com",
            Subject = "Help please",
            Message = "Something is broken.",
        };

        await model.OnPostAsync();

        model.Sent.Should().BeTrue();
        queue.Reader.TryRead(out var msg).Should().BeTrue();
        msg!.To.Should().Be("support@orr.com");
        msg.ReplyTo.Should().Be("user@example.com");
        msg.TextBody.Should().Contain("Something is broken.");
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task OnPostAsync_honeypot_filled_drops_submission_silently()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@orr.com");
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
        var model = CreateModel(queue, inbox: "support@orr.com", signedInEmail: "me@example.com");

        model.OnGet();

        model.Input.Email.Should().Be("me@example.com");
    }

    private static SupportModel CreateModel(
        EmailDispatchQueue queue, string? inbox, string? signedInEmail = null,
        ISupportBotNotifier? botNotifier = null)
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
            botNotifier ?? new NoopSupportBotNotifier(),
            Options.Create(new SupportSettings { Inbox = inbox }),
            NullStringLocalizer<SharedResource>.Instance,
            NullLogger<SupportModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext { User = user },
                ViewData = new ViewDataDictionary(
                    new EmptyModelMetadataProvider(), new ModelStateDictionary())
            }
        };
    }

    private static IReadOnlyList<string> GoalsOn(SupportModel model) =>
        (model.ViewData[AnalyticsGoal.ViewDataKey] as IEnumerable<AnalyticsGoal>)?
            .Select(g => g.Name).ToList() ?? [];

    private sealed class NoopSupportBotNotifier : ISupportBotNotifier
    {
        public void NotifySupportTicket(string email, string subject, string message) { }
    }

    private sealed class RecordingSupportBotNotifier : ISupportBotNotifier
    {
        public List<(string Email, string Subject, string Message)> Tickets { get; } = [];

        public void NotifySupportTicket(string email, string subject, string message)
            => Tickets.Add((email, subject, message));
    }

    [Fact]
    public async Task OnPostAsync_valid_submission_notifies_the_bot_channel()
    {
        var queue = new EmailDispatchQueue();
        var bot = new RecordingSupportBotNotifier();
        var model = CreateModel(queue, inbox: "support@orr.com", botNotifier: bot);
        model.Input = new SupportModel.InputModel
        {
            Email = "user@example.com",
            Subject = "Help please",
            Message = "Something is broken.",
        };

        await model.OnPostAsync();

        bot.Tickets.Should().Contain(("user@example.com", "Help please", "Something is broken."));
    }

    [Fact]
    public async Task OnPostAsync_honeypot_filled_does_not_notify_the_bot()
    {
        var queue = new EmailDispatchQueue();
        var bot = new RecordingSupportBotNotifier();
        var model = CreateModel(queue, inbox: "support@orr.com", botNotifier: bot);
        model.Input = new SupportModel.InputModel
        {
            Email = "bot@spam.com",
            Subject = "Buy now",
            Message = "spam",
            Website = "http://spam.example",
        };

        await model.OnPostAsync();

        bot.Tickets.Should().BeEmpty("a dropped submission is not a support ticket");
    }

    [Fact]
    public async Task OnPostAsync_invalid_form_does_not_notify_the_bot()
    {
        var queue = new EmailDispatchQueue();
        var bot = new RecordingSupportBotNotifier();
        var model = CreateModel(queue, inbox: "support@orr.com", botNotifier: bot);
        model.Input = new SupportModel.InputModel
        {
            Email = "not-an-email",
            Subject = "Hi",
            Message = "Question.",
        };
        model.ModelState.AddModelError("Input.Email", "invalid email");

        await model.OnPostAsync();

        bot.Tickets.Should().BeEmpty("an invalid submission never becomes a bot event");
    }

    [Fact]
    public async Task OnPostAsync_without_configured_inbox_still_notifies_the_bot()
    {
        // BOT-001: the Telegram channel is independent — an empty Support:Inbox must not
        // silence it (and the email channel's misbehavior stays its own concern).
        var queue = new EmailDispatchQueue();
        var bot = new RecordingSupportBotNotifier();
        var model = CreateModel(queue, inbox: null, botNotifier: bot);
        model.Input = new SupportModel.InputModel
        {
            Email = "user@example.com",
            Subject = "Hi",
            Message = "Question.",
        };

        await model.OnPostAsync();

        model.Sent.Should().BeTrue();
        queue.Reader.TryRead(out _).Should().BeFalse();
        bot.Tickets.Should().Contain(("user@example.com", "Hi", "Question."));
    }

    [Fact]
    public async Task OnPostAsync_valid_submission_reports_the_support_goal()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@orr.com");
        model.Input = new SupportModel.InputModel
        {
            Email = "user@example.com",
            Subject = "Help please",
            Message = "Something is broken.",
        };

        await model.OnPostAsync();

        GoalsOn(model).Should().Equal(AnalyticsGoals.SupportSubmitted);
    }

    [Fact]
    public async Task OnPostAsync_honeypot_reports_no_goal()
    {
        var queue = new EmailDispatchQueue();
        var model = CreateModel(queue, inbox: "support@orr.com");
        model.Input = new SupportModel.InputModel
        {
            Email = "bot@spam.com",
            Subject = "Buy now",
            Message = "spam",
            Website = "http://spam.example",
        };

        await model.OnPostAsync();

        // The bot sees the same success screen, but a dropped submission is not a conversion.
        GoalsOn(model).Should().BeEmpty();
    }

    [Fact]
    public async Task OnPostAsync_without_configured_inbox_reports_no_goal()
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

        GoalsOn(model).Should().BeEmpty("nothing was delivered, so nothing converted");
    }
}
