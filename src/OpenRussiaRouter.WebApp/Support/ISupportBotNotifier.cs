namespace OpenRussiaRouter.WebApp.Support;

public interface ISupportBotNotifier
{
    /// <summary>
    /// Queues a support-ticket event for the Telegram bot. Contractually never throws and
    /// never performs I/O — the bot channel must not be able to break the /support form,
    /// the email path or the response time.
    /// </summary>
    void NotifySupportTicket(string email, string subject, string message);
}
