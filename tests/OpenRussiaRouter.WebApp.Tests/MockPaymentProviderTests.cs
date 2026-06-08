using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

public class MockPaymentProviderTests
{
    private static MockPaymentProvider CreateProvider(string sharedSecret = "dev")
    {
        var settings = Options.Create(new PaymentSettings
        {
            ActiveProvider = "mock",
            Mock = new MockPaymentSettings { SharedSecret = sharedSecret }
        });
        return new MockPaymentProvider(settings, NullLogger<MockPaymentProvider>.Instance);
    }

    private static IHeaderDictionary Headers(string? signature) =>
        new HeaderDictionary
        {
            ["X-Mock-Signature"] = signature ?? string.Empty
        };

    [Fact]
    public void Name_is_mock()
    {
        var provider = CreateProvider();
        provider.Name.Should().Be("mock");
    }

    [Fact]
    public async Task CreateAsync_returns_pay_url_pointing_at_local_fake_checkout()
    {
        var provider = CreateProvider();
        var request = new PaymentRequest(
            OrderId: "pay-123",
            Amount: 100m,
            Currency: "RUB",
            Description: "Top-up");

        var created = await provider.CreateAsync(request, CancellationToken.None);

        created.ProviderPaymentId.Should().StartWith("mock_");
        created.PayUrl.Should().StartWith("/pay/mock/pay-123");
        created.ExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_emits_distinct_provider_payment_ids()
    {
        var provider = CreateProvider();
        var request = new PaymentRequest("pay-1", 10m, "USD", "x");

        var a = await provider.CreateAsync(request, CancellationToken.None);
        var b = await provider.CreateAsync(request, CancellationToken.None);

        a.ProviderPaymentId.Should().NotBe(b.ProviderPaymentId);
    }

    [Fact]
    public void Verify_rejects_missing_signature_header()
    {
        var provider = CreateProvider();
        provider.Verify("body", Headers(signature: null))
            .Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_rejects_wrong_signature()
    {
        var provider = CreateProvider(sharedSecret: "dev");
        provider.Verify("body", Headers("wrong"))
            .Should().Be(WebhookVerificationResult.Invalid);
    }

    [Fact]
    public void Verify_accepts_correct_shared_secret()
    {
        var provider = CreateProvider(sharedSecret: "dev");
        provider.Verify("body", Headers("dev"))
            .Should().Be(WebhookVerificationResult.Valid);
    }

    [Fact]
    public void Parse_reads_paid_event_with_amount_and_currency()
    {
        var provider = CreateProvider();
        var payload = MockPaymentProvider.BuildWebhookPayload(
            providerPaymentId: "mock_abc",
            orderId: "pay-123",
            eventType: "paid",
            amount: 100m,
            currency: "RUB",
            providerEventId: "evt-1");

        var evt = provider.Parse(payload);

        evt.ProviderPaymentId.Should().Be("mock_abc");
        evt.OrderId.Should().Be("pay-123");
        evt.EventType.Should().Be("paid");
        evt.ConfirmedAmount.Should().Be(100m);
        evt.ConfirmedCurrency.Should().Be("RUB");
        evt.ProviderEventId.Should().Be("evt-1");
    }

    [Fact]
    public void Parse_lowercases_event_type()
    {
        var provider = CreateProvider();
        var payload = MockPaymentProvider.BuildWebhookPayload(
            providerPaymentId: "mock_x",
            orderId: "pay-1",
            eventType: "PAID",
            amount: 1m,
            currency: "USD");

        provider.Parse(payload).EventType.Should().Be("paid");
    }

    [Fact]
    public void Parse_throws_on_empty_body()
    {
        var provider = CreateProvider();
        var act = () => provider.Parse("");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Parse_throws_when_event_type_missing()
    {
        var provider = CreateProvider();
        var act = () => provider.Parse("{\"provider_payment_id\":\"x\"}");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task GetPaymentAsync_returns_Created_for_stateless_mock()
    {
        var provider = CreateProvider();
        var status = await provider.GetPaymentAsync("mock_anything", CancellationToken.None);
        status.Status.Should().Be(ProviderPaymentStatusType.Created);
    }
}
