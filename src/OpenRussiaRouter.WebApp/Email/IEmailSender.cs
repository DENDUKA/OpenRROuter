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
    IReadOnlyList<EmailAttachment>? Attachments = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
