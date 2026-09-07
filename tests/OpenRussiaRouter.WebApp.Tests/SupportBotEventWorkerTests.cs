using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenRussiaRouter.WebApp.Support;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// BOT-001 integration-point tests: the background worker relays accepted /support
/// submissions to the bot's closed HTTP API on synthetic data with a scripted handler.
/// Covers the response contract (202/200/503/4xx), timeouts, network failures, limited
/// retries with a stable event id, and independence of the delivery loop from the form.
/// </summary>
public class SupportBotEventWorkerTests
{
    private const string Endpoint = "http://support-bot:8080/v1/events/support";
    private const string Token = "synthetic-inbound-token";

    private static SupportBotSettings Settings(int maxAttempts = 4, int retryDelaySeconds = 0) => new()
    {
        Enabled = true,
        Endpoint = Endpoint,
        AuthToken = Token,
        MaxAttempts = maxAttempts,
        RetryDelaySeconds = retryDelaySeconds,
    };

    private static (SupportBotEventQueue Queue, SupportBotEventWorker Worker, StubFactory Factory, ScriptedHandler Handler, FakeTimeProvider Time)
        CreateWorker(SupportBotSettings settings)
    {
        var queue = new SupportBotEventQueue();
        var handler = new ScriptedHandler();
        var factory = new StubFactory(handler);
        var time = new FakeTimeProvider();
        var worker = new SupportBotEventWorker(
            queue, factory, NullLogger<SupportBotEventWorker>.Instance, Options.Create(settings), time);
        return (queue, worker, factory, handler, time);
    }

    private static SupportBotEvent Event(string id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") =>
        new(id, DateTimeOffset.UtcNow, "user@example.com", "Не проходит оплата", "Текст обращения.");

    private static async Task RunWorkerAsync(Func<Task> whileRunning)
    {
        // Real-time safety net only — assertions below wait for exact request counts.
        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        var body = whileRunning();
        var done = await Task.WhenAny(body, timeout);
        done.Should().Be(body, "the worker should finish within the test timeout");
    }

    private static async Task WaitForRequestsAsync(ScriptedHandler handler, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (handler.Captured.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        handler.Captured.Count.Should().Be(count);
    }

    [Fact]
    public async Task Accepted_202_posts_the_contract_json_with_bearer_auth()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings());
        queue.TryEnqueue(Event("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 1);
            await worker.StopAsync(CancellationToken.None);
        });

        var request = handler.Captured.Single();
        request.Method.Should().Be("POST");
        request.Uri.Should().Be(Endpoint);
        request.Authorization.Should().Be($"Bearer {Token}");
        request.ContentType.Should().StartWith("application/json");

        using var doc = JsonDocument.Parse(request.Body!);
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        root.GetProperty("type").GetString().Should().Be("support_ticket");
        root.GetProperty("occurredAt").GetString().Should().NotBeNullOrWhiteSpace();
        var payload = root.GetProperty("payload");
        payload.GetProperty("email").GetString().Should().Be("user@example.com");
        payload.GetProperty("subject").GetString().Should().Be("Не проходит оплата");
        payload.GetProperty("message").GetString().Should().Be("Текст обращения.");
    }

    [Fact]
    public async Task Registration_event_posts_to_registration_endpoint_with_minimal_payload()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings());
        queue.TryEnqueue(SupportBotEvent.UserRegistered("cccccccccccccccccccccccccccccccc", DateTimeOffset.UtcNow, "newuser@example.com"));

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 1);
            await worker.StopAsync(CancellationToken.None);
        });

        var request = handler.Captured.Single();
        request.Method.Should().Be("POST");
        request.Uri.Should().Be("http://support-bot:8080/v1/events/registration");
        request.Authorization.Should().Be($"Bearer {Token}");

        using var doc = JsonDocument.Parse(request.Body!);
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("cccccccccccccccccccccccccccccccc");
        root.GetProperty("type").GetString().Should().Be("user_registered");
        var payload = root.GetProperty("payload");
        payload.GetProperty("email").GetString().Should().Be("newuser@example.com");
        payload.TryGetProperty("subject", out _).Should().BeFalse();
        payload.TryGetProperty("message", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Topup_event_posts_to_topup_endpoint_with_payload()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings());
        queue.TryEnqueue(SupportBotEvent.TopUpCompleted(
            "topup-order-1", DateTimeOffset.UtcNow, "payer@example.com", 1500m, 16.25m, "Rollypay (СБП)"));

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 1);
            await worker.StopAsync(CancellationToken.None);
        });

        var request = handler.Captured.Single();
        request.Method.Should().Be("POST");
        request.Uri.Should().Be("http://support-bot:8080/v1/events/topup");
        request.Authorization.Should().Be($"Bearer {Token}");

        using var doc = JsonDocument.Parse(request.Body!);
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("topup-order-1");
        root.GetProperty("type").GetString().Should().Be("topup_completed");
        var payload = root.GetProperty("payload");
        payload.GetProperty("email").GetString().Should().Be("payer@example.com");
        payload.GetProperty("amountRub").GetDecimal().Should().Be(1500m);
        payload.GetProperty("creditedUsd").GetDecimal().Should().Be(16.25m);
        payload.GetProperty("provider").GetString().Should().Be("Rollypay (СБП)");
    }

    [Fact]
    public async Task Duplicate_200_is_terminal_success_no_retry()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings());
        handler.Script(200);
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 1);
            await Task.Delay(200); // no further attempts must appear
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(1, "a duplicate is a success, not a retryable outcome");
    }

    [Theory]
    [InlineData(400)] // invalid_event — our payload violates the contract, retrying cannot fix it
    [InlineData(401)] // auth failure — configuration problem, not transient
    [InlineData(413)] // body over the bot's limit — permanent
    public async Task Permanent_rejection_is_not_retried(int status)
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings(maxAttempts: 4, retryDelaySeconds: 60));
        handler.Script(status);
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 1);
            await Task.Delay(200); // a retry would need a 60s delay, none must be scheduled
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(1);
    }

    [Fact]
    public async Task Queue_full_503_is_retried_with_the_same_stable_id_then_dropped()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings(maxAttempts: 3, retryDelaySeconds: 0));
        handler.Script(503).Script(503).Script(503);
        queue.TryEnqueue(Event("cccccccccccccccccccccccccccccccc"));

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 3);
            await Task.Delay(200); // attempt limit reached, nothing more may be sent
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(3, "retries are limited by MaxAttempts");
        handler.Captured.Select(c => JsonDocument.Parse(c.Body!).RootElement.GetProperty("id").GetString())
            .Should().AllBe("cccccccccccccccccccccccccccccccc",
                "the id is stable across retries so the bot deduplicates, not double-notifies");
    }

    [Fact]
    public async Task Queue_full_503_then_202_recovers_within_the_attempt_limit()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings(maxAttempts: 3, retryDelaySeconds: 0));
        handler.Script(503).Script(202);
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 2);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(2, "one failure, one successful retry");
    }

    [Fact]
    public async Task Network_error_is_retried_then_dropped()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings(maxAttempts: 2, retryDelaySeconds: 0));
        handler.Script(new HttpRequestException("connection refused"))
              .Script(new HttpRequestException("connection refused"));
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 2);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(2, "network errors are retried, then the event is dropped (best-effort)");
    }

    [Fact]
    public async Task Request_timeout_is_retried_then_dropped()
    {
        // TaskCanceledException without request cancellation is how HttpClient.Timeout surfaces.
        var (queue, worker, _, handler, _) = CreateWorker(Settings(maxAttempts: 2, retryDelaySeconds: 0));
        handler.Script(new TaskCanceledException())
              .Script(new TaskCanceledException());
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 2);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(2, "timeouts are retried like network failures");
    }

    [Fact]
    public async Task Retry_waits_the_configured_delay()
    {
        var (queue, worker, _, handler, time) = CreateWorker(Settings(maxAttempts: 2, retryDelaySeconds: 60));
        handler.Script(503).Script(202);
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 1);

            // The second attempt must not happen before the virtual 60s retry delay elapses.
            await Task.Delay(100);
            handler.Captured.Should().HaveCount(1, "the retry must wait out RetryDelaySeconds");

            for (int i = 0; i < 65; i++)
            {
                time.Advance(TimeSpan.FromSeconds(1));
                await Task.Yield();
            }
            await WaitForRequestsAsync(handler, 2);
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().HaveCount(2);
    }

    [Fact]
    public async Task Unconfigured_worker_drops_events_without_any_http_call()
    {
        // Defense in depth: a queued event that outlived a config change must not produce
        // a request with a null URL/token.
        var settings = new SupportBotSettings { Enabled = true, AuthToken = Token };
        var (queue, worker, _, handler, _) = CreateWorker(settings);
        queue.TryEnqueue(Event());

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Should().BeEmpty();
        queue.Reader.TryRead(out _).Should().BeFalse("the event was consumed (dropped), not wedged in the queue");
    }

    [Fact]
    public async Task Worker_drains_multiple_events_one_by_one()
    {
        var (queue, worker, _, handler, _) = CreateWorker(Settings());
        queue.TryEnqueue(Event("dddddddddddddddddddddddddddddddd"));
        queue.TryEnqueue(Event("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"));

        await RunWorkerAsync(async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForRequestsAsync(handler, 2);
            await worker.StopAsync(CancellationToken.None);
        });

        handler.Captured.Select(c => JsonDocument.Parse(c.Body!).RootElement.GetProperty("id").GetString())
            .Should().Equal("dddddddddddddddddddddddddddddddd", "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
    }

    /// <summary>HttpClient factory over a scripted handler; never disposes the shared handler.</summary>
    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly ScriptedHandler _handler;

        public StubFactory(ScriptedHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed record CapturedRequest(string Method, string? Uri, string? Authorization, string? ContentType, string? Body);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly Queue<object> _script = new();

        public List<CapturedRequest> Captured { get; } = [];

        public ScriptedHandler Script(int statusCode)
        {
            lock (_lock) _script.Enqueue(statusCode);
            return this;
        }

        public ScriptedHandler Script(Exception exception)
        {
            lock (_lock) _script.Enqueue(exception);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (_lock)
            {
                Captured.Add(new CapturedRequest(
                    request.Method.Method,
                    request.RequestUri?.ToString(),
                    request.Headers.Authorization?.ToString(),
                    request.Content?.Headers.ContentType?.MediaType,
                    body));

                if (_script.Count > 0)
                {
                    var next = _script.Dequeue();
                    if (next is Exception exception)
                        throw exception;
                    return new HttpResponseMessage((HttpStatusCode)(int)next)
                    {
                        Content = new StringContent("{}")
                    };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{}") };
        }
    }
}
