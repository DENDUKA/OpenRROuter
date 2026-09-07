namespace OpenRussiaRouter.WebApp.Support;

/// <summary>
/// Options for the support Telegram-bot channel (BOT-001), bound from configuration
/// section "Support:Bot". The whole channel is off unless <see cref="Enabled"/> is set.
/// <see cref="AuthToken"/> is a shared secret: it must live only in env/secret storage,
/// never in appsettings.json and never in logs. Delivery is best-effort by design —
/// bounded in-memory queue, limited attempts, event dropped on final failure; the email
/// channel is independent and remains the durable one.
/// </summary>
public sealed class SupportBotSettings
{
    public const string SectionName = "Support:Bot";

    /// <summary>Master switch; false (default) disables the channel entirely.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Full POST URL of the bot's event endpoint, e.g. http://support-bot:8080/v1/events/support.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Shared bearer secret for the bot's HTTP API. Env/secret only, like Alerting keys.</summary>
    public string? AuthToken { get; set; }

    /// <summary>Per-request timeout for the bot HTTP call. Default 10s.</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>Maximum delivery attempts per event (retried only on 503/timeout/network). Default 4.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Delay between delivery attempts of the same event. Default 60s.</summary>
    public int RetryDelaySeconds { get; set; } = 60;
}
