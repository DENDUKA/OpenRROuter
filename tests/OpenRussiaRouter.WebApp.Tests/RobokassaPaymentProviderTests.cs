using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Payments.Robokassa;

namespace OpenRussiaRouter.WebApp.Tests;

public class RobokassaPaymentProviderTests
{
    private const string Login = "rawrter";
    private const string Pass1 = "pass-one";
    private const string NotifyUrl = "https://rawrter.com/webhook/robokassa";
    private const string OrderId = "9b1f6a52-2d1c-4f5e-8d0e-1a2b3c4d5e6f";

    // Stands in for Robokassa's signing key: the provider takes the public half through its internal ctor.
    private static readonly RSA TestKey = RSA.Create(2048);

    [Fact]
    public void Name_is_robokassa_and_settles_in_RUB()
    {
        var provider = CreateProvider();

        provider.Name.Should().Be("robokassa");
        provider.SettlementCurrency.Should().Be("RUB");
    }

    [Fact]
    public async Task CreateAsync_builds_signed_url_with_receipt_and_once_encoded_ResultUrl2()
    {
        var provider = CreateProvider();

        var created = await provider.CreateAsync(
            new PaymentRequest(OrderId, 1500m, "RUB", "Top-up 15 USD"), CancellationToken.None);

        created.ProviderPaymentId.Should().Be(OrderId);
        var uri = new Uri(created.PayUrl);
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["MerchantLogin"].ToString().Should().Be(Login);
        query["OutSum"].ToString().Should().Be("1500.00");
        query.ContainsKey("IsTest").Should().BeFalse();

        // Receipt travels url-encoded twice (once for the signature string, once for the query) and
        // the signature is computed over the once-encoded string.
        var receipt = query["Receipt"].ToString();
        receipt.Should().StartWith("%7B");
        var receiptJson = Uri.UnescapeDataString(receipt);
        receiptJson.Should().Contain("\"payment_method\":\"full_payment\"");
        receiptJson.Should().Contain("\"payment_object\":\"service\"");
        receiptJson.Should().Contain("\"sum\":1500,");

        // ResultUrl2 is encoded exactly once, both in the query and in the signature string.
        var encodedResultUrl2 = Uri.EscapeDataString($"{NotifyUrl}?order={OrderId}");
        uri.Query.Should().Contain("&ResultUrl2=" + encodedResultUrl2);
        query["ResultUrl2"].ToString().Should().Be($"{NotifyUrl}?order={OrderId}");

        var invId = query["InvId"].ToString();
        long.Parse(invId).Should().BePositive();
        query["SignatureValue"].ToString()
            .Should().Be(Md5($"{Login}:1500.00:{invId}:{receipt}:{encodedResultUrl2}:{Pass1}"));
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
    public async Task CreateAsync_refuses_to_run_without_NotifyUrl()
    {
        var provider = CreateProvider(notifyUrl: "");

        var act = () => provider.CreateAsync(
            new PaymentRequest(OrderId, 10m, "RUB", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*NotifyUrl*");
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
    public void Verify_accepts_a_correctly_signed_notification()
    {
        CreateProvider().Verify(SignedJws(OrderId, "OK"), new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Valid);
    }

    [Fact]
    public void Verify_accepts_the_token_wrapped_in_a_form_field_or_quotes()
    {
        var provider = CreateProvider();
        var jws = SignedJws(OrderId, "OK");

        provider.Verify($"notification={jws}", new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Valid);
        provider.Verify($"\"{jws}\"\n", new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Valid);
    }

    [Fact]
    public void Verify_rejects_a_tampered_payload()
    {
        var parts = SignedJws(OrderId, "OK", incSum: "10.00").Split('.');
        var forged = Payload(new { shop = Login, invId = "1", incSum = "9999.00", state = "OK" });

        CreateProvider().Verify($"{parts[0]}.{forged}.{parts[2]}", new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_a_non_RS256_header()
    {
        var parts = SignedJws(OrderId, "OK").Split('.');
        var header = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"none"}"""));

        CreateProvider().Verify($"{header}.{parts[1]}.{parts[2]}", new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_with_the_pinned_Robokassa_key_rejects_a_token_signed_by_anyone_else()
    {
        var pinned = new RobokassaPaymentProvider(
            new StaticOptionsMonitor<PaymentSettings>(new PaymentSettings()),
            NullLogger<RobokassaPaymentProvider>.Instance);

        pinned.Verify(SignedJws(OrderId, "OK"), new HeaderDictionary())
            .Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_garbage_and_unsigned_tokens()
    {
        var provider = CreateProvider();

        provider.Verify("not-a-jws", new HeaderDictionary()).Should().Be(WebhookVerificationResult.Invalid);
        provider.Verify("OutSum=1&InvId=2", new HeaderDictionary()).Should().Be(WebhookVerificationResult.Invalid);
        provider.Verify(UnsignedJws(OrderId, "OK"), new HeaderDictionary()).Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Parse_returns_paid_event_bound_to_the_order_from_the_query()
    {
        var provider = CreateProvider();

        var evt = provider.Parse(UnsignedJws(OrderId, "OK", incSum: "100.00"), OrderQuery(OrderId));

        evt.EventType.Should().Be(PaymentWebhookEventTypes.Paid);
        evt.ProviderPaymentId.Should().Be(OrderId);
        evt.OrderId.Should().Be(OrderId);
        evt.ConfirmedAmount.Should().Be(100m);
        evt.ConfirmedCurrency.Should().Be("RUB");
        evt.ProviderEventId.Should().Be("op-key-1");
    }

    [Fact]
    public void Parse_treats_a_non_OK_state_as_a_no_op_event()
    {
        var evt = CreateProvider().Parse(UnsignedJws(OrderId, "Pending"), OrderQuery(OrderId));

        evt.EventType.Should().Be(PaymentWebhookEventTypes.Created);
    }

    [Fact]
    public void Parse_rejects_an_order_id_that_does_not_match_the_signed_invId()
    {
        var provider = CreateProvider();

        var act = () => provider.Parse(UnsignedJws(OrderId, "OK"), OrderQuery("someone-elses-order"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match*");
    }

    [Fact]
    public void Parse_rejects_missing_order_and_foreign_shop()
    {
        var provider = CreateProvider();

        var noOrder = () => provider.Parse(UnsignedJws(OrderId, "OK"));
        var foreignShop = () => provider.Parse(UnsignedJws(OrderId, "OK", shop: "other"), OrderQuery(OrderId));

        noOrder.Should().Throw<InvalidOperationException>();
        foreignShop.Should().Throw<InvalidOperationException>().WithMessage("*different shop*");
    }

    [Fact]
    public void WebhookAckText_is_OK_followed_by_invoice_id()
    {
        var provider = CreateProvider();
        var evt = provider.Parse(UnsignedJws(OrderId, "OK"), OrderQuery(OrderId));

        provider.WebhookAckText(evt).Should().Be("OK" + RobokassaPaymentProvider.InvoiceId(OrderId));
    }

    private static IQueryCollection OrderQuery(string orderId) =>
        new QueryCollection(new Dictionary<string, StringValues> { ["order"] = orderId });

    /// <summary>A JWS-shaped token with a bogus signature: enough for Parse, which only runs after Verify.</summary>
    private static string UnsignedJws(
        string orderId, string state, string incSum = "10.00", string shop = Login) =>
        string.Join('.',
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"RS256"}""")),
            Payload(new
            {
                shop,
                opKey = "op-key-1",
                invId = RobokassaPaymentProvider.InvoiceId(orderId).ToString(),
                incSum,
                state,
            }),
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(256)));

    private static string SignedJws(string orderId, string state, string incSum = "10.00")
    {
        var unsigned = UnsignedJws(orderId, state, incSum);
        var signingInput = unsigned[..unsigned.LastIndexOf('.')];
        var signature = TestKey.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    private static string Payload(object data) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { data }));

    private static string InvId(string payUrl) =>
        QueryHelpers.ParseQuery(new Uri(payUrl).Query)["InvId"].ToString();

    private static string Md5(string value) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static RobokassaPaymentProvider CreateProvider(bool testMode = false, string notifyUrl = NotifyUrl) =>
        new(
            new StaticOptionsMonitor<PaymentSettings>(new PaymentSettings
            {
                Robokassa = new RobokassaSettings
                {
                    MerchantLogin = Login,
                    Password1 = Pass1,
                    NotifyUrl = notifyUrl,
                    TestMode = testMode,
                },
            }),
            NullLogger<RobokassaPaymentProvider>.Instance,
            TestKey.ExportSubjectPublicKeyInfo());

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
