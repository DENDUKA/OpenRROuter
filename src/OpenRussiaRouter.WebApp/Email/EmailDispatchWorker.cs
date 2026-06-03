using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Email;

/// <summary>
/// Drains the email queue sequentially. Per message: up to 3 send attempts with
/// 1s/2s/4s backoff; on final failure the message is dropped (logged). A fixed
/// BatchDelayMs pause after each message throttles throughput to respect the
/// SMTP provider's rate limits (default 1000ms ≈ 1 msg/s).
/// </summary>
public class EmailDispatchWorker : BackgroundService
{
    private readonly IEmailDispatchQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchWorker> _logger;
    private readonly EmailSettings _settings;

    public EmailDispatchWorker(
        IEmailDispatchQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailDispatchWorker> logger,
        IOptions<EmailSettings> settings)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("EmailDispatchWorker started.");

        await foreach (var message in _queue.Reader.ReadAllAsync(ct).WithCancellation(ct))
        {
            bool processed = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    await sender.SendAsync(message, ct);
                    processed = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw; // shutdown
                }
                catch (Exception ex) when (attempt < 3)
                {
                    var delayMs = 1000 * (1 << (attempt - 1)); // 1s, 2s, 4s
                    _logger.LogWarning(ex, "Send to {To} failed (attempt {Attempt}/3), retry in {DelayMs}ms",
                        message.To, attempt, delayMs);
                    await Task.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Send to {To} failed after 3 attempts, discarding", message.To);
                    processed = true; // stop retrying
                    break;
                }
            }

            if (processed && _settings.BatchDelayMs > 0)
                await Task.Delay(_settings.BatchDelayMs, ct);
        }

        _logger.LogInformation("EmailDispatchWorker stopped.");
    }
}
