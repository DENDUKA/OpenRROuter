using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace OpenRussiaRouter.WebApp.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IOptions<EmailSettings> settings,
        IConfiguration configuration,
        ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        var bodyPart = new TextPart(TextFormat.Plain) { Text = message.TextBody };

        if (message.Attachments?.Any() != true)
        {
            mimeMessage.Body = bodyPart;
        }
        else
        {
            var multipart = new Multipart("mixed") { bodyPart };
            foreach (var attachment in message.Attachments)
            {
                var part = new MimePart(attachment.ContentType)
                {
                    Content = new MimeContent(new MemoryStream(attachment.Content)),
                    ContentDisposition = new ContentDisposition(
                        attachment.Inline ? ContentDisposition.Inline : ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = attachment.FileName
                };
                if (attachment.Inline && !string.IsNullOrEmpty(attachment.ContentId))
                    part.ContentId = attachment.ContentId;
                multipart.Add(part);
            }
            mimeMessage.Body = multipart;
        }

        using var smtp = new SmtpClient();
        try
        {
            // 465 => implicit TLS; otherwise STARTTLS (or None if UseSsl=false).
            var socketOptions = _settings.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : _settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

            // Credentials live in secrets/env, NOT in EmailSettings/appsettings.
            var username = _configuration["Email:SmtpUsername"];
            var password = _configuration["Email:SmtpPassword"];

            await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, socketOptions, ct);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                await smtp.AuthenticateAsync(username, password, ct);
            else
                _logger.LogWarning("SMTP username/password not configured, skipping auth");

            await smtp.SendAsync(mimeMessage, ct);
            await smtp.DisconnectAsync(true, ct);

            _logger.LogInformation("Email sent to {To} '{Subject}'", message.To, message.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} '{Subject}'", message.To, message.Subject);
            throw;
        }
    }
}
