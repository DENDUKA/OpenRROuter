using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.Payments.YandexPay;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class YandexPayWebhookVerifierTests
{
    private const string MerchantId = "merchant-abc";
    private const string Kid = "key-1";

    /// <summary>
    /// Yandex Pay signs notifications without an <c>exp</c> claim. The JWT handler requires one by
    /// default and rejects the token with IDX10225 before ever checking the signature, which
    /// silently 401'd every real webhook and left payments to be rescued by reconciliation
    /// 10-15 minutes later.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_accepts_a_signed_token_without_an_expiry_claim()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = SignNotification(key, includeExpiry: false);

        var result = await CreateVerifier(key).VerifyAsync(jwt, CancellationToken.None);

        result.Should().NotBeNull();
        result!.OrderId.Should().Be("payment-1");
    }

    [Fact]
    public async Task VerifyAsync_still_accepts_a_token_that_does_carry_an_expiry()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = SignNotification(key, includeExpiry: true);

        var result = await CreateVerifier(key).VerifyAsync(jwt, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_rejects_a_token_signed_by_a_different_key()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var published = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = SignNotification(attacker, includeExpiry: false);

        // Dropping the expiry requirement must not weaken the part that actually establishes trust.
        var result = await CreateVerifier(published).VerifyAsync(jwt, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_rejects_a_token_for_another_merchant()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = SignNotification(key, includeExpiry: false, merchantId: "someone-else");

        var result = await CreateVerifier(key).VerifyAsync(jwt, CancellationToken.None);

        result.Should().BeNull();
    }

    private static string SignNotification(
        ECDsa signingKey,
        bool includeExpiry,
        string merchantId = MerchantId)
    {
        var securityKey = new ECDsaSecurityKey(signingKey) { KeyId = Kid };
        var header = new JwtHeader(new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256));
        header["kid"] = Kid;

        var payload = new JwtPayload
        {
            ["event"] = "ORDER_STATUS_UPDATED",
            ["eventTime"] = DateTimeOffset.UtcNow.ToString("O"),
            ["merchantId"] = merchantId,
            ["order"] = new Dictionary<string, object>
            {
                ["orderId"] = "payment-1",
                ["paymentStatus"] = "PENDING",
            },
        };

        if (includeExpiry)
        {
            payload["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        }

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }

    private static YandexPayWebhookVerifier CreateVerifier(ECDsa publishedKey)
    {
        var options = Options.Create(new PaymentSettings
        {
            YandexPay = new YandexPaySettings
            {
                Enabled = true,
                Sandbox = true,
                MerchantId = MerchantId,
            },
        });

        var factory = new StubHttpClientFactory(new JwksHandler(publishedKey));

        return new YandexPayWebhookVerifier(
            new YandexPayJwkProvider(factory, options, NullLogger<YandexPayJwkProvider>.Instance),
            new YandexPayClient(factory, options, NullLogger<YandexPayClient>.Instance),
            options,
            NullLogger<YandexPayWebhookVerifier>.Instance);
    }

    /// <summary>Serves the public half of <paramref name="key"/> as the Yandex Pay JWKS document.</summary>
    private sealed class JwksHandler(ECDsa key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var parameters = key.ExportParameters(includePrivateParameters: false);
            var jwks = JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "EC",
                        crv = "P-256",
                        kid = Kid,
                        use = "sig",
                        alg = "ES256",
                        x = Base64UrlEncoder.Encode(parameters.Q.X),
                        y = Base64UrlEncoder.Encode(parameters.Q.Y),
                    },
                },
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jwks, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
