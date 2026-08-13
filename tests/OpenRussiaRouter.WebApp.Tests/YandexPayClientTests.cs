using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class YandexPayClientTests
{
    private const string MerchantId = "merchant-abc";

    /// <summary>
    /// Locks the documented create-order response shape: the pay link lives directly under
    /// <c>data</c>. Reading it from <c>data.order</c> — the shape the GET endpoint uses — turned
    /// every successful 200 into an empty 502 at the checkout endpoint.
    /// </summary>
    [Fact]
    public async Task CreateOrderAsync_reads_payment_url_from_data()
    {
        var handler = new StubHandler(_ => Json(
            """{"code":200,"status":"success","data":{"paymentUrl":"https://sandbox.pay.yandex.ru/checkout/tok_x"}}"""));

        var url = await CreateClient(handler).CreateOrderAsync(
            orderId: "payment-1",
            amount: 100m,
            successRedirectUrl: "https://rwt.test/ok",
            failRedirectUrl: "https://rwt.test/fail",
            ttlSeconds: 1800,
            CancellationToken.None);

        url.Should().Be("https://sandbox.pay.yandex.ru/checkout/tok_x");

        handler.Paths.Should().ContainSingle().Which.Should().Be("/api/merchant/v1/orders");
        handler.Authorizations[0].Should().Be($"Api-Key {MerchantId}");
        handler.Bodies[0].Should().Contain("\"orderId\":\"payment-1\"");
        handler.Bodies[0].Should().Contain("\"amount\":\"100.00\"");
        handler.Bodies[0].Should().Contain("\"currencyCode\":\"RUB\"");
    }

    [Fact]
    public async Task CreateOrderAsync_throws_with_the_body_when_the_response_carries_no_pay_url()
    {
        // The pre-fix shape: a 200 whose link is nested one level too deep.
        var handler = new StubHandler(_ => Json(
            """{"status":"success","data":{"order":{"paymentUrl":"https://sandbox.pay.yandex.ru/x"}}}"""));

        var act = () => CreateClient(handler).CreateOrderAsync(
            "payment-1", 100m, "https://rwt.test/ok", "https://rwt.test/fail", 1800, CancellationToken.None);

        // The body has to travel with the exception: without it a shape drift is
        // indistinguishable from a rejected order.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no paymentUrl*")
            .WithMessage("*\"order\"*");
    }

    [Fact]
    public async Task CreateOrderAsync_throws_when_the_api_rejects_the_order()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"status":"fail","reasonCode":"FORBIDDEN"}""", Encoding.UTF8, "application/json"),
        });

        var act = () => CreateClient(handler).CreateOrderAsync(
            "payment-1", 100m, "https://rwt.test/ok", "https://rwt.test/fail", 1800, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Unauthorized*")
            .WithMessage("*FORBIDDEN*");
    }

    private static YandexPayClient CreateClient(StubHandler handler)
    {
        var settings = new PaymentSettings
        {
            YandexPay = new YandexPaySettings
            {
                Enabled = true,
                Sandbox = true,
                MerchantId = MerchantId,
            },
        };

        return new YandexPayClient(
            new StubHttpClientFactory(handler),
            Options.Create(settings),
            NullLogger<YandexPayClient>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<string> Paths { get; } = new();
        public List<string?> Authorizations { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
