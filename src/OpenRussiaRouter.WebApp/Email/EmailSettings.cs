namespace OpenRussiaRouter.WebApp.Email;

/// <summary>
/// Non-secret email options. Bind from configuration section "Email".
/// SmtpUsername / SmtpPassword are NOT here on purpose — they are read from
/// secrets/env by SmtpEmailSender so credentials never sit in a settings object
/// that might get logged or serialized.
/// </summary>
public class EmailSettings
{
    public string SmtpHost { get; set; } = null!;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string SenderAddress { get; set; } = null!;
    public string SenderName { get; set; } = "OpenRussiaRouter";

    /// <summary>
    /// Pause after each sent message in the dispatch worker, to stay under the
    /// SMTP provider's rate limit (default 1000ms ≈ 1 msg/s).
    /// </summary>
    public int BatchDelayMs { get; set; } = 1000;
}
