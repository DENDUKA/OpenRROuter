using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Payments.Rollypay;

namespace OpenRussiaRouter.WebApp.Tests;

public class RollypayPaymentProviderTests
{
    private const string ApiKey = "key_live_abc";
    private const string Secret = "test-signing-secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);

    // ---- CreateAsync -------------------------------------------------------

    [Fact]
    public async Task CreateAsync_posts_amount_as_F2_string_and_returns_provider_ref_and_pay_url()
    {
        var handler = new StubHandler(_ => Json(
            """{"payment_id":"pay_abc","order_id":"order-1","status":"created","pay_url":"https://pay.rollypay.io/tok_x","expires_at":"2030-01-01T00:00:00Z"}"""));
        var provider = CreateProvider(handler);

        var created = await provider.CreateAsync(
            new PaymentRequest(OrderId: "order-1", Amount: 1500m, Currency: "RUB", Description: "Top-up"),
            CancellationToken.None);

        created.ProviderPaymentId.Should().Be("pay_abc");
        created.PayUrl.Should().Be("https://pay.rollypay.io/tok_x");
        created.ExpiresAtUtc.Should().Be(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        handler.Paths.Should().ContainSingle().Which.Should().Be("/api/v1/payments");
        handler.ApiKeys[0].Should().Be(ApiKey);
        handler.Bodies[0].Should().Contain("\"amount\":\"1500.00\"");
        handler.Bodies[0].Should().Contain("\"payment_currency\":\"RUB\"");
        handler.Bodies[0].Should().Contain("\"order_id\":\"order-1\"");
        handler.Bodies[0].Should().Contain("\"payment_method\":\"sbp\"");
    }

    [Fact]
    public async Task CreateAsync_omits_test_flag_when_test_mode_is_off()
    {
        var handler = new StubHandler(_ => Json("""{"payment_id":"pay_1","status":"created","pay_url":"https://pay/x"}"""));
        var provider = CreateProvider(handler, testMode: false);

        await provider.CreateAsync(new PaymentRequest("order-1", 10m, "RUB", "x"), CancellationToken.None);

        // Absent rather than "test":false — a live payment must not depend on Rollypay's parsing of it.
        handler.Bodies[0].Should().NotContain("\"test\"");
    }

    [Fact]
    public async Task CreateAsync_marks_payment_as_test_when_test_mode_is_on()
    {
        var handler = new StubHandler(_ => Json("""{"payment_id":"pay_1","status":"created","pay_url":"https://pay/x"}"""));
        var provider = CreateProvider(handler, testMode: true);

        await provider.CreateAsync(new PaymentRequest("order-1", 10m, "RUB", "x"), CancellationToken.None);

        handler.Bodies[0].Should().Contain("\"test\":true");
    }

    [Fact]
    public async Task CreateAsync_sends_user_id_as_customer_id()
    {
        var handler = new StubHandler(_ => Json("""{"payment_id":"pay_1","status":"created","pay_url":"https://pay/x"}"""));
        var provider = CreateProvider(handler);

        await provider.CreateAsync(
            new PaymentRequest("order-1", 10m, "RUB", "x", UserId: "user-42"),
            CancellationToken.None);

        handler.Bodies[0].Should().Contain("\"customer_id\":\"user-42\"");
    }

    [Fact]
    public async Task CreateAsync_omits_customer_id_when_no_user_id()
    {
        var handler = new StubHandler(_ => Json("""{"payment_id":"pay_1","status":"created","pay_url":"https://pay/x"}"""));
        var provider = CreateProvider(handler);

        await provider.CreateAsync(new PaymentRequest("order-1", 10m, "RUB", "x"), CancellationToken.None);

        handler.Bodies[0].Should().NotContain("customer_id");
    }

    [Fact]
    public void SettlementCurrency_is_RUB_because_sbp_settles_in_rubles()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));

        provider.SettlementCurrency.Should().Be("RUB");
    }

    [Fact]
    public async Task CreateAsync_sends_fresh_nonce_per_request()
    {
        var handler = new StubHandler(_ => Json(
            """{"payment_id":"pay_1","status":"created","pay_url":"https://pay/x"}"""));
        var provider = CreateProvider(handler);
        var request = new PaymentRequest("order-1", 10m, "RUB", "x");

        await provider.CreateAsync(request, CancellationToken.None);
        await provider.CreateAsync(request, CancellationToken.None);

        handler.Nonces.Should().HaveCount(2);
        handler.Nonces[0].Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(handler.Nonces[0], out _).Should().BeTrue();
        handler.Nonces[0].Should().NotBe(handler.Nonces[1]);
    }

    [Fact]
    public async Task CreateAsync_throws_when_pay_url_missing()
    {
        var handler = new StubHandler(_ => Json("""{"payment_id":"pay_1","status":"created"}"""));
        var provider = CreateProvider(handler);

        var act = () => provider.CreateAsync(new PaymentRequest("o", 1m, "RUB", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_throws_when_api_key_not_configured()
    {
        var handler = new StubHandler(_ => Json("{}"));
        var provider = CreateProvider(handler, apiKey: "");

        var act = () => provider.CreateAsync(new PaymentRequest("o", 1m, "RUB", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_maps_4xx_to_RollypayApiException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"bad amount"}""", Encoding.UTF8, "application/json"),
        });
        var provider = CreateProvider(handler);

        var act = () => provider.CreateAsync(new PaymentRequest("o", 1m, "RUB", "x"), CancellationToken.None);

        (await act.Should().ThrowAsync<RollypayApiException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAsync_maps_5xx_to_RollypayUnavailableException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(handler);

        var act = () => provider.CreateAsync(new PaymentRequest("o", 1m, "RUB", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<RollypayUnavailableException>();
    }

    // ---- GetPaymentAsync ---------------------------------------------------

    [Theory]
    [InlineData("paid", ProviderPaymentStatusType.Paid)]
    [InlineData("created", ProviderPaymentStatusType.Created)]
    [InlineData("processing", ProviderPaymentStatusType.Pending)]
    [InlineData("canceled", ProviderPaymentStatusType.Canceled)]
    [InlineData("expired", ProviderPaymentStatusType.Expired)]
    [InlineData("refunded", ProviderPaymentStatusType.Refunded)]
    [InlineData("chargeback", ProviderPaymentStatusType.ChargedBack)]
    [InlineData("weird", ProviderPaymentStatusType.Unknown)]
    public async Task GetPaymentAsync_maps_status(string apiStatus, ProviderPaymentStatusType expected)
    {
        var handler = new StubHandler(_ => Json(
            $$"""{"payment_id":"pay_1","status":"{{apiStatus}}","amount":"1500.00","payment_currency":"RUB"}"""));
        var provider = CreateProvider(handler);

        var status = await provider.GetPaymentAsync("pay_1", CancellationToken.None);

        status.Status.Should().Be(expected);
        status.ConfirmedAmount.Should().Be(1500.00m);
        status.ConfirmedCurrency.Should().Be("RUB");
        handler.Paths[0].Should().Be("/api/v1/payments/pay_1");
    }

    // ---- Verify ------------------------------------------------------------

    [Fact]
    public void Verify_accepts_valid_signature()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        const string body = """{"event_type":"payment.paid"}""";
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{ts}.{body}");

        provider.Verify(body, Headers(ts, sig)).Should().Be(WebhookVerificationResult.Valid);
    }

    [Fact]
    public void Verify_rejects_bad_signature()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        var ts = Now.ToUnixTimeSeconds().ToString();

        provider.Verify("body", Headers(ts, "deadbeef")).Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_when_secret_not_configured()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")), secret: "");
        var ts = Now.ToUnixTimeSeconds().ToString();

        provider.Verify("body", Headers(ts, "abcd")).Should().Be(WebhookVerificationResult.Invalid);
    }

    // ---- Parse -------------------------------------------------------------

    [Fact]
    public void Parse_maps_paid_event_with_amount_and_currency()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        const string body = """{"event_type":"payment.paid","payment_id":"pay_1","order_id":"order-1","amount":"1500.00","currency":"RUB"}""";

        var evt = provider.Parse(body);

        evt.EventType.Should().Be(PaymentWebhookEventTypes.Paid);
        evt.ProviderPaymentId.Should().Be("pay_1");
        evt.OrderId.Should().Be("order-1");
        evt.ConfirmedAmount.Should().Be(1500.00m);
        evt.ConfirmedCurrency.Should().Be("RUB");
        evt.ProviderEventId.Should().BeNull();
        evt.Reason.Should().BeNull();
    }

    [Theory]
    [InlineData("payment.canceled", PaymentWebhookEventTypes.Canceled)]
    [InlineData("payment.refunded", PaymentWebhookEventTypes.Refunded)]
    [InlineData("payment.chargeback", PaymentWebhookEventTypes.Chargeback)]
    [InlineData("payment.created", PaymentWebhookEventTypes.Created)]
    public void Parse_maps_known_event_types(string raw, string expected)
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        var body = $$"""{"event_type":"{{raw}}","payment_id":"pay_1","status":"{{raw}}"}""";

        provider.Parse(body).EventType.Should().Be(expected);
    }

    [Fact]
    public void Parse_passes_through_account_level_event_for_processor_to_ignore()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        const string body = """{"event_type":"payout.completed","payment_id":"pay_1"}""";

        provider.Parse(body).EventType.Should().Be("payout.completed");
    }

    [Fact]
    public void Parse_throws_on_empty_body()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        var act = () => provider.Parse("");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Parse_throws_when_event_type_missing()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        var act = () => provider.Parse("""{"payment_id":"pay_1"}""");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Parse_throws_on_invalid_json()
    {
        var provider = CreateProvider(new StubHandler(_ => Json("{}")));
        var act = () => provider.Parse("not json");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Name_is_rollypay()
    {
        CreateProvider(new StubHandler(_ => Json("{}"))).Name.Should().Be("rollypay");
    }

    // ---- helpers -----------------------------------------------------------

    private static RollypayPaymentProvider CreateProvider(
        StubHandler handler, string apiKey = ApiKey, string secret = Secret, bool testMode = false)
    {
        var settings = new PaymentSettings
        {
            ActiveProvider = "rollypay",
            Rollypay = new RollypaySettings
            {
                BaseUrl = "https://rollypay.test",
                ApiKey = apiKey,
                SigningSecret = secret,
                TestMode = testMode,
            },
        };

        return new RollypayPaymentProvider(
            new StubHttpClientFactory(handler),
            new StaticOptionsMonitor<PaymentSettings>(settings),
            NullLogger<RollypayPaymentProvider>.Instance,
            new FakeTimeProvider(Now));
    }

    private static IHeaderDictionary Headers(string? timestamp, string? signature) =>
        new HeaderDictionary
        {
            ["X-Timestamp"] = timestamp ?? string.Empty,
            ["X-Signature"] = signature ?? string.Empty,
        };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<string> Paths { get; } = new();
        public List<string?> ApiKeys { get; } = new();
        public List<string?> Nonces { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            ApiKeys.Add(request.Headers.TryGetValues("X-API-Key", out var k) ? k.FirstOrDefault() : null);
            Nonces.Add(request.Headers.TryGetValues("X-Nonce", out var n) ? n.FirstOrDefault() : null);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("https://rollypay.test/") };
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
