namespace OpenRussiaRouter.WebApp.Support;

/// <summary>
/// A validated /support submission handed to the Telegram-bot channel. The <see cref="Id"/>
/// is generated once per submission and reused across every delivery attempt, so the bot's
/// in-memory deduplication sees a repeat rather than a new ticket. Payload mirrors the
/// agreed HTTP contract (BOT-001): id, type, occurredAt, payload{email, subject, message} —
/// deliberately no user id, role or user agent (those stay in the email path only).
/// </summary>
public sealed record SupportBotEvent(
    string Id,
    DateTimeOffset OccurredAt,
    string Email,
    string Subject,
    string Message)
{
    /// <summary>The only event type accepted by the bot in BOT-001.</summary>
    public const string Type = "support_ticket";
}
