using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Support;

/// <summary>
/// Entry point for the /support page: turns a validated submission into a queued bot
/// event (BOT-001). A disabled channel is a silent no-op; an enabled but misconfigured
/// one logs a safe error (never the secret values, the submitter address or the message
/// text) and drops the event. Every failure mode is swallowed — the email path and the
/// form response must stay unaffected by the bot channel.
/// </summary>
public sealed class SupportBotNotifier : ISupportBotNotifier
{
    private readonly ISupportBotEventQueue _queue;
    private readonly SupportBotSettings _settings;
    private readonly ILogger<SupportBotNotifier> _logger;

    public SupportBotNotifier(
        ISupportBotEventQueue queue,
        IOptions<SupportBotSettings> settings,
        ILogger<SupportBotNotifier> logger)
    {
        _queue = queue;
        _settings = settings.Value;
        _logger = logger;
    }

    public void NotifySupportTicket(string email, string subject, string message)
    {
        try
        {
            if (!_settings.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(_settings.Endpoint) || string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                _logger.LogError(
                    "Support bot channel is enabled but Endpoint/AuthToken is not configured; ticket notification dropped.");
                return;
            }

            // One id per submission, reused by the worker across all retries, so the bot
            // deduplicates a repeat instead of creating a second notification.
            var supportEvent = new SupportBotEvent(
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, email, subject, message);

            if (!_queue.TryEnqueue(supportEvent))
                _logger.LogWarning("Support bot event queue is full; ticket notification dropped (best-effort channel).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue a support ticket notification.");
        }
    }

    public void NotifyUserRegistered(string email)
    {
        try
        {
            if (!_settings.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(_settings.Endpoint) || string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                _logger.LogError(
                    "Support bot channel is enabled but Endpoint/AuthToken is not configured; registration notification dropped.");
                return;
            }

            var regEvent = SupportBotEvent.UserRegistered(
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, email);

            if (!_queue.TryEnqueue(regEvent))
                _logger.LogWarning("Support bot event queue is full; registration notification dropped (best-effort channel).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue a user registration notification.");
        }
    }

    public void NotifyTopUpCompleted(string email, decimal amountRub, decimal creditedUsd, string provider, string? paymentId = null)
    {
        try
        {
            if (!_settings.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(_settings.Endpoint) || string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                _logger.LogError(
                    "Support bot channel is enabled but Endpoint/AuthToken is not configured; topup notification dropped.");
                return;
            }

            var eventId = !string.IsNullOrWhiteSpace(paymentId) ? $"topup-{paymentId}" : Guid.NewGuid().ToString("N");
            var topupEvent = SupportBotEvent.TopUpCompleted(
                eventId, DateTimeOffset.UtcNow, email, amountRub, creditedUsd, provider);

            if (!_queue.TryEnqueue(topupEvent))
                _logger.LogWarning("Support bot event queue is full; topup notification dropped (best-effort channel).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue a topup notification.");
        }
    }
}
