namespace OpenRussiaRouter.WebApp.Email;

public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content,
    bool Inline = false,
    string? ContentId = null);

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    IReadOnlyList<EmailAttachment>? Attachments = null)
{
    /// <summary>
    /// Optional Reply-To address. When set, replies from the recipient's mail client go
    /// here instead of the platform sender. Used by the support form so staff replies to a
    /// ticket land straight in the user's inbox. Init-only so existing positional call sites
    /// are unaffected.
    /// </summary>
    public string? ReplyTo { get; init; }
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
