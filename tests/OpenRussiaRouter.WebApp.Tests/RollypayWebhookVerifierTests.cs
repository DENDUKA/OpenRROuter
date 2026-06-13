using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.Payments.Rollypay;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Golden HMAC tests — the single most valuable test in the Rollypay integration. If these are
/// green, real webhooks verify; a broken HMAC would otherwise only surface from production logs.
/// </summary>
public class RollypayWebhookVerifierTests
{
    private const string Secret = "test-signing-secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);
    private const string Body = """{"event_type":"payment.paid","payment_id":"pay_1","amount":"1500.00","currency":"RUB"}""";

    private static string Ts => Now.ToUnixTimeSeconds().ToString();

    [Fact]
    public void Verify_valid_signature_returns_Valid()
    {
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{Ts}.{Body}");

        RollypayWebhookVerifier.Verify(Secret, Body, Ts, sig, Now)
            .Should().Be(RollypayVerificationResult.Valid);
    }

    [Fact]
    public void Verify_tampered_body_returns_BadSignature()
    {
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{Ts}.{Body}");
        var tampered = Body.Replace("pay_1", "pay_2");

        RollypayWebhookVerifier.Verify(Secret, tampered, Ts, sig, Now)
            .Should().Be(RollypayVerificationResult.BadSignature);
    }

    [Fact]
    public void Verify_expired_timestamp_returns_ExpiredTimestamp()
    {
        var oldTs = Now.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{oldTs}.{Body}");

        RollypayWebhookVerifier.Verify(Secret, Body, oldTs, sig, Now)
            .Should().Be(RollypayVerificationResult.ExpiredTimestamp);
    }

    [Theory]
    [InlineData(null, "abcd")]
    [InlineData("abcd", null)]
    [InlineData("", "abcd")]
    [InlineData("abcd", "")]
    public void Verify_missing_headers_returns_MissingHeaders(string? ts, string? sig)
    {
        RollypayWebhookVerifier.Verify(Secret, Body, ts, sig, Now)
            .Should().Be(RollypayVerificationResult.MissingHeaders);
    }

    [Fact]
    public void Verify_non_numeric_timestamp_returns_BadTimestamp()
    {
        RollypayWebhookVerifier.Verify(Secret, Body, "not-a-number", "abcd", Now)
            .Should().Be(RollypayVerificationResult.BadTimestamp);
    }

    [Fact]
    public void Verify_non_hex_signature_returns_BadSignature()
    {
        RollypayWebhookVerifier.Verify(Secret, Body, Ts, "zz-not-hex", Now)
            .Should().Be(RollypayVerificationResult.BadSignature);
    }

    [Fact]
    public void Verify_empty_secret_returns_SecretNotConfigured()
    {
        RollypayWebhookVerifier.Verify("", Body, Ts, "abcd", Now)
            .Should().Be(RollypayVerificationResult.SecretNotConfigured);
    }
}
