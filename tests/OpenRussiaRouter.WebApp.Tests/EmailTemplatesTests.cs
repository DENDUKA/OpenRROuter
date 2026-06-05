using FluentAssertions;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Tests;

public class EmailTemplatesTests
{
    [Fact]
    public void InviteEmail_carries_recipient_and_link()
    {
        var link = "https://app.example.com/invite/raw-token-123";

        var msg = EmailTemplates.InviteEmail("user@example.com", link);

        msg.To.Should().Be("user@example.com");
        msg.Subject.Should().NotBeNullOrWhiteSpace();
        msg.TextBody.Should().Contain(link);
        msg.Attachments.Should().BeNull();
    }

    [Fact]
    public void VerificationEmail_carries_recipient_and_code()
    {
        var msg = EmailTemplates.VerificationEmail("user@example.com", "123456");

        msg.To.Should().Be("user@example.com");
        msg.Subject.Should().NotBeNullOrWhiteSpace();
        msg.TextBody.Should().Contain("123456");
        msg.Attachments.Should().BeNull();
    }
}

public class EmailBroadcastBuildMessagesTests
{
    private static UserInfo User(string id, string email) =>
        new(id, email, "User", "Active", DateTimeOffset.UtcNow, null, 10);

    [Fact]
    public void BuildMessages_creates_one_message_per_user_with_same_subject_and_body()
    {
        var users = new[] { User("1", "a@x.com"), User("2", "b@x.com"), User("3", "c@x.com") };

        var messages = EmailBroadcastService.BuildMessages(users, "Hello", "Body text");

        messages.Should().HaveCount(3);
        messages.Select(m => m.To).Should().BeEquivalentTo(new[] { "a@x.com", "b@x.com", "c@x.com" });
        messages.Should().OnlyContain(m => m.Subject == "Hello" && m.TextBody == "Body text");
    }

    [Fact]
    public void BuildMessages_skips_users_with_blank_email()
    {
        var users = new[] { User("1", "a@x.com"), User("2", ""), User("3", "   ") };

        var messages = EmailBroadcastService.BuildMessages(users, "S", "B");

        messages.Should().ContainSingle().Which.To.Should().Be("a@x.com");
    }
}
