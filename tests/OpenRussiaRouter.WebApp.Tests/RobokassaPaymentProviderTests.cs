using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Payments.Robokassa;

namespace OpenRussiaRouter.WebApp.Tests;

public class RobokassaPaymentProviderTests
{
    private const string Login = "rawrter";
    private const string Pass1 = "pass-one";
    private const string Pass2 = "pass-two";
    private const string OrderId = "9b1f6a52-2d1c-4f5e-8d0e-1a2b3c4d5e6f";

    [Fact]
    public void Name_is_robokassa_and_settles_in_RUB()
    {
        var provider = CreateProvider();

        provider.Name.Should().Be("robokassa");
        provider.SettlementCurrency.Should().Be("RUB");
    }

    [Fact]
    public async Task CreateAsync_builds_signed_url_with_receipt_and_order_id_in_Shp_param()
    {
        var provider = CreateProvider();

        var created = await provider.CreateAsync(
            new PaymentRequest(OrderId, 1500m, "RUB", "Top-up 15 USD"), CancellationToken.None);

        created.ProviderPaymentId.Should().Be(OrderId);
        var query = QueryHelpers.ParseQuery(new Uri(created.PayUrl).Query);
        query["MerchantLogin"].ToString().Should().Be(Login);
        query["OutSum"].ToString().Should().Be("1500.00");
        query["Shp_orderid"].ToString().Should().Be(OrderId);
        query.ContainsKey("IsTest").Should().BeFalse();

        // Receipt travels url-encoded twice (once for the signature string, once for the query) and
        // the signature is computed over the once-encoded string.
        var receipt = query["Receipt"].ToString();
        receipt.Should().StartWith("%7B");
        var invId = query["InvId"].ToString();
        long.Parse(invId).Should().BePositive();
        var expected = Md5($"{Login}:1500.00:{invId}:{receipt}:{Pass1}:Shp_orderid={OrderId}");
        query["SignatureValue"].ToString().Should().Be(expected);

        var receiptJson = Uri.UnescapeDataString(receipt);
        receiptJson.Should().Contain("\"payment_method\":\"full_payment\"");
        receiptJson.Should().Contain("\"payment_object\":\"service\"");
        receiptJson.Should().Contain("\"sum\":1500,");
    }

    [Fact]
    public async Task CreateAsync_marks_test_payments_with_IsTest()
    {
        var provider = CreateProvider(testMode: true);

        var created = await provider.CreateAsync(
            new PaymentRequest(OrderId, 10m, "RUB", "x"), CancellationToken.None);

        QueryHelpers.ParseQuery(new Uri(created.PayUrl).Query)["IsTest"].ToString().Should().Be("1");
    }

    [Fact]
    public async Task CreateAsync_rejects_non_rub_currency()
    {
        var provider = CreateProvider();

        var act = () => provider.CreateAsync(
            new PaymentRequest(OrderId, 10m, "USD", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_invoice_id_is_stable_per_order_and_differs_between_orders()
    {
        var provider = CreateProvider();

        var a1 = await provider.CreateAsync(new PaymentRequest(OrderId, 10m, "RUB", "x"), CancellationToken.None);
        var a2 = await provider.CreateAsync(new PaymentRequest(OrderId, 10m, "RUB", "x"), CancellationToken.None);
        var b = await provider.CreateAsync(new PaymentRequest("another-order", 10m, "RUB", "x"), CancellationToken.None);

        InvId(a1.PayUrl).Should().Be(InvId(a2.PayUrl));
        InvId(a1.PayUrl).Should().NotBe(InvId(b.PayUrl));
    }

    [Fact]
    public void Verify_accepts_correct_signature_and_is_case_insensitive_on_hex()
    {
        var provider = CreateProvider();
        var upper = Md5Upper($"100.000000:450009:{Pass2}:Shp_orderid={OrderId}");

        provider.Verify(ResultBody("100.000000", "450009", OrderId, upper), new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Valid);
        provider.Verify(ResultBody("100.000000", "450009", OrderId, upper.ToLowerInvariant()), new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Valid);
    }

    [Fact]
    public void Verify_rejects_tampered_amount()
    {
        var provider = CreateProvider();
        var signature = Md5Upper($"100.000000:450009:{Pass2}:Shp_orderid={OrderId}");

        var body = ResultBody("999.000000", "450009", OrderId, signature);

        provider.Verify(body, new HeaderDictionary()).Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_signature_made_with_password_one()
    {
        var provider = CreateProvider();
        var body = ResultBody("100.000000", "450009", OrderId, Md5Upper($"100.000000:450009:{Pass1}:Shp_orderid={OrderId}"));

        provider.Verify(body, new HeaderDictionary()).Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_everything_when_password_is_not_configured()
    {
        var provider = CreateProvider(pass2: "");
        var body = ResultBody("100.000000", "450009", OrderId, Md5Upper($"100.000000:450009::Shp_orderid={OrderId}"));

        provider.Verify(body, new HeaderDictionary()).Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_missing_signature_or_garbage_body()
    {
        var provider = CreateProvider();

        provider.Verify($"OutSum=1&InvId=2&Shp_orderid={OrderId}", new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Invalid);
        provider.Verify("not-a-form-body", new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Parse_returns_paid_event_with_amount_in_rub_and_order_id()
    {
        var provider = CreateProvider();
        var body = ResultBody("100.000000", "450009", OrderId, "ignored-by-parse");

        var evt = provider.Parse(body);

        evt.EventType.Should().Be(PaymentWebhookEventTypes.Paid);
        evt.ProviderPaymentId.Should().Be(OrderId);
        evt.OrderId.Should().Be(OrderId);
        evt.ConfirmedAmount.Should().Be(100m);
        evt.ConfirmedCurrency.Should().Be("RUB");
    }

    [Fact]
    public void Parse_throws_when_order_id_is_missing()
    {
        var provider = CreateProvider();

        var act = () => provider.Parse("OutSum=1.000000&InvId=5&SignatureValue=x");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task WebhookAckText_is_OK_followed_by_invoice_id_matching_create()
    {
        var provider = CreateProvider();
        var evt = provider.Parse(ResultBody("100.000000", "450009", OrderId, "x"));

        var created = await provider.CreateAsync(
            new PaymentRequest(OrderId, 100m, "RUB", "x"), CancellationToken.None);

        provider.WebhookAckText(evt).Should().Be("OK" + InvId(created.PayUrl));
    }

    private static string InvId(string payUrl) =>
        QueryHelpers.ParseQuery(new Uri(payUrl).Query)["InvId"].ToString();

    private static string ResultBody(string outSum, string invId, string orderId, string signature) =>
        $"OutSum={outSum}&InvId={invId}&Fee=0&EMail=a%40b.c&SignatureValue={signature}&PaymentMethod=BankCard&IncCurrLabel=BankCardPSR&Shp_orderid={orderId}";

    private static string Md5(string value) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Md5Upper(string value) => Md5(value).ToUpperInvariant();

    private static RobokassaPaymentProvider CreateProvider(bool testMode = false, string pass2 = Pass2) =>
        new(
            new StaticOptionsMonitor<PaymentSettings>(new PaymentSettings
            {
                Robokassa = new RobokassaSettings
                {
                    MerchantLogin = Login,
                    Password1 = Pass1,
                    Password2 = pass2,
                    TestMode = testMode,
                },
            }),
            NullLogger<RobokassaPaymentProvider>.Instance);

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
