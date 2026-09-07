namespace OpenRussiaRouter.WebApp.Support;

/// <summary>
/// An event handed to the Telegram-bot channel. The <see cref="Id"/> is generated once
/// per occurrence and reused across every delivery attempt, so the bot's in-memory
/// deduplication sees a repeat rather than a new ticket/notification.
/// </summary>
public sealed record SupportBotEvent(
    string Id,
    DateTimeOffset OccurredAt,
    string Email,
    string? Subject,
    string? Message,
    string EventType = SupportBotEvent.SupportTicketType)
{
    public const string SupportTicketType = "support_ticket";
    public const string UserRegisteredType = "user_registered";

    /// <summary>Backward-compatible alias for <see cref="SupportTicketType"/>.</summary>
    public const string Type = SupportTicketType;

    public SupportBotEvent(string id, DateTimeOffset occurredAt, string email, string subject, string message)
        : this(id, occurredAt, email, subject, message, SupportTicketType)
    {
    }

    public static SupportBotEvent SupportTicket(string id, DateTimeOffset occurredAt, string email, string subject, string message)
        => new(id, occurredAt, email, subject, message, SupportTicketType);

    public static SupportBotEvent UserRegistered(string id, DateTimeOffset occurredAt, string email)
        => new(id, occurredAt, email, null, null, UserRegisteredType);
}
