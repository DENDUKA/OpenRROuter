using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenRussiaRouter.WebApp.Api;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class PaymentWebhookEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PaymentWebhookEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_before_json_parsing()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/mock")
        {
            Content = new StringContent("not-json")
        };
        request.Headers.Add("X-Mock-Signature", "wrong");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Streamed_body_over_64kb_is_rejected_with_413()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/mock")
        {
            Content = new UnknownLengthContent(new byte[PaymentWebhookEndpoint.MaxBodyBytes + 1])
        };
        request.Headers.Add("X-Mock-Signature", "dev");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Forged_forwarded_for_values_do_not_bypass_webhook_rate_limit()
    {
        for (var requestNumber = 1; requestNumber <= 60; requestNumber++)
        {
            using var request = CreateForgedForwardedForRequest(requestNumber);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rejectedRequest = CreateForgedForwardedForRequest(61);
        using var rejectedResponse = await _client.SendAsync(rejectedRequest);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
    }

    private static HttpRequestMessage CreateForgedForwardedForRequest(int requestNumber)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/mock")
        {
            Content = new StringContent("not-json")
        };
        request.Headers.Add("X-Mock-Signature", "wrong");
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-For",
            $"198.51.100.{requestNumber}, 172.20.0.2");
        return request;
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
