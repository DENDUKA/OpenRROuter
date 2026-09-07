using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Support;

/// <summary>
/// Drains the support-bot event queue sequentially and POSTs each event to the bot's
/// closed HTTP API (<c>POST /v1/events/support</c>, Bearer auth). Response contract
/// (BOT-001): <c>202</c> = accepted into the bot's queue (NOT Telegram delivery),
/// <c>200</c> = duplicate — both are terminal success; <c>503</c> = bot queue full /
/// notifications disabled / bot misconfigured — retried within the attempt limit;
/// any other status (400 invalid_event, 401, 413 …) is permanent, the event is dropped.
/// Timeouts and network errors are retried too: the event id is stable, so the bot
/// deduplicates a repeat instead of notifying twice. On exhausted attempts the event
/// is dropped with an error log — best-effort by design, the email channel is independent.
/// Logs carry the event id and status code only: no addresses, message texts or tokens.
/// </summary>
public sealed class SupportBotEventWorker : BackgroundService
{
    public const string HttpClientName = "SupportBot";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISupportBotEventQueue _queue;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SupportBotEventWorker> _logger;
    private readonly SupportBotSettings _settings;
    private readonly TimeProvider _timeProvider;

    public SupportBotEventWorker(
        ISupportBotEventQueue queue,
        IHttpClientFactory httpClientFactory,
        ILogger<SupportBotEventWorker> logger,
        IOptions<SupportBotSettings> settings,
        TimeProvider? timeProvider = null)
    {
        _queue = queue;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _settings = settings.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("SupportBotEventWorker started.");

        await foreach (var supportEvent in _queue.Reader.ReadAllAsync(ct).WithCancellation(ct))
        {
            await DeliverAsync(supportEvent, ct);
        }

        _logger.LogInformation("SupportBotEventWorker stopped.");
    }

    private async Task DeliverAsync(SupportBotEvent supportEvent, CancellationToken ct)
    {
        // Defense in depth: the notifier already gates on configuration, but a queue that
        // outlived a config change must not produce requests with a null URL/token.
        if (string.IsNullOrWhiteSpace(_settings.Endpoint) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            _logger.LogError("Support bot event {EventId} dropped: Endpoint/AuthToken is not configured.", supportEvent.Id);
            return;
        }

        var maxAttempts = Math.Max(1, _settings.MaxAttempts);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                switch (await SendOnceAsync(supportEvent, ct))
                {
                    case SendOutcome.Accepted:
                        _logger.LogInformation("Support bot accepted event {EventId} (queued, not yet delivered).", supportEvent.Id);
                        return;
                    case SendOutcome.Duplicate:
                        _logger.LogInformation("Support bot reported event {EventId} as duplicate.", supportEvent.Id);
                        return;
                    case SendOutcome.Rejected:
                        return; // already logged with the status code
                    case SendOutcome.RetryLater:
                        _logger.LogWarning("Support bot refused event {EventId} with 503 (attempt {Attempt}/{MaxAttempts}); retrying.",
                            supportEvent.Id, attempt, maxAttempts);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // shutdown, not a delivery failure
            }
            catch (Exception ex)
            {
                // Timeout or network failure: the outcome is unknown, but the event id is
                // stable, so a repeat is deduplicated by the bot rather than notified twice.
                _logger.LogWarning(ex, "Support bot request for event {EventId} failed (attempt {Attempt}/{MaxAttempts}); retrying.",
                    supportEvent.Id, attempt, maxAttempts);
            }

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(_settings.RetryDelaySeconds), _timeProvider, ct);
        }

        _logger.LogError("Support bot delivery of event {EventId} failed after {MaxAttempts} attempts; event dropped (best-effort channel).",
            supportEvent.Id, maxAttempts);
    }

    private string ResolveEndpoint(SupportBotEvent supportEvent)
    {
        if (supportEvent.EventType == SupportBotEvent.UserRegisteredType)
        {
            if (!string.IsNullOrWhiteSpace(_settings.RegistrationEndpoint))
                return _settings.RegistrationEndpoint;

            if (!string.IsNullOrWhiteSpace(_settings.Endpoint) &&
                _settings.Endpoint.EndsWith("/support", StringComparison.OrdinalIgnoreCase))
            {
                return _settings.Endpoint[..^8] + "/registration";
            }
        }

        return _settings.Endpoint ?? "";
    }

    private async Task<SendOutcome> SendOnceAsync(SupportBotEvent supportEvent, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);

        var endpoint = ResolveEndpoint(supportEvent);
        object payloadDto = supportEvent.EventType == SupportBotEvent.UserRegisteredType
            ? new UserRegisteredPayloadDto(supportEvent.Email)
            : new SupportBotPayloadDto(supportEvent.Email, supportEvent.Subject ?? "", supportEvent.Message ?? "");

        var eventDto = new SupportBotEventDto(
            supportEvent.Id,
            supportEvent.EventType,
            supportEvent.OccurredAt,
            payloadDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(eventDto, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AuthToken);

        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;

        return status switch
        {
            202 => SendOutcome.Accepted,
            200 => SendOutcome.Duplicate,
            503 => SendOutcome.RetryLater,
            _ => Reject(supportEvent.Id, status)
        };
    }

    private SendOutcome Reject(string eventId, int status)
    {
        _logger.LogError("Support bot permanently rejected event {EventId}: HTTP {Status}.", eventId, status);
        return SendOutcome.Rejected;
    }

    private enum SendOutcome
    {
        Accepted,
        Duplicate,
        RetryLater,
        Rejected
    }

    private sealed record SupportBotEventDto(string Id, string Type, DateTimeOffset OccurredAt, object Payload);

    private sealed record SupportBotPayloadDto(string Email, string Subject, string Message);

    private sealed record UserRegisteredPayloadDto(string Email);
}
