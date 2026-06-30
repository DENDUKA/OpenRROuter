using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.CollectorService.Application.OpenRouter;

namespace OpenRussiaRouter.CollectorService.Tests.OpenRouter;

public class OpenRouterCatalogClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static OpenRouterCatalogClient Build(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://openrouter.ai/") };
        return new OpenRouterCatalogClient(http, NullLogger<OpenRouterCatalogClient>.Instance);
    }

    [Fact]
    public async Task LoadAsync_FlattensEntries_AndPricing()
    {
        const string body = """
        {"data":[{"id":"openai/gpt-4o","name":"OpenAI: GPT-4o","author_display_name":"OpenAI",
        "description":"desc","context_length":128000,
        "architecture":{"input_modalities":["text","image"],"output_modalities":["text"]},
        "pricing":{"prompt":"0.000005","completion":"0.000015","input_cache_read":"0.0000025"}}]}
        """;
        var entries = await Build(HttpStatusCode.OK, body).LoadAsync(CancellationToken.None);

        entries.Should().ContainSingle();
        var e = entries[0];
        e.Slug.Should().Be("openai/gpt-4o");
        e.ContextLength.Should().Be(128000);
        e.InputModalities.Should().Equal("text", "image");
        e.PricePrompt.Should().Be("0.000005");
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_OnHttpError()
    {
        var entries = await Build(HttpStatusCode.InternalServerError, "boom").LoadAsync(CancellationToken.None);
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_OnMalformedJson()
    {
        var entries = await Build(HttpStatusCode.OK, "{ not json").LoadAsync(CancellationToken.None);
        entries.Should().BeEmpty();
    }
}
