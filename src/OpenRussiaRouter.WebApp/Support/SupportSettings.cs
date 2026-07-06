namespace OpenRussiaRouter.WebApp.Support;

/// <summary>
/// Options for the customer support channel. Bind from configuration section "Support".
/// Both values are optional: with no <see cref="Inbox"/> the contact form still accepts a
/// submission but logs an error instead of delivering it (misconfiguration is never leaked
/// to the user), and with no <see cref="TelegramUrl"/> the Telegram button is hidden.
/// </summary>
public sealed class SupportSettings
{
    public const string SectionName = "Support";

    /// <summary>Staff mailbox that receives support-form submissions (e.g. support@…).</summary>
    public string? Inbox { get; set; }

    /// <summary>Public Telegram link (e.g. https://t.me/…) shown as an alternative channel.</summary>
    public string? TelegramUrl { get; set; }
}
