using System.Net;
using System.Text;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.Anthropic;

namespace OpenRussiaRouter.ApiGateway.Tests.Anthropic;

public sealed class AnthropicUsageAndStreamTests
{
    [Fact]
    public void AnthropicUsageExtractor_StandardUsage_ExtractsWithCache()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "model": "claude-3-7-sonnet-20250219",
            "content": [{"type": "text", "text": "Hello"}],
            "usage": {
                "input_tokens": 50,
                "output_tokens": 15,
                "cache_creation_input_tokens": 10,
                "cache_read_input_tokens": 20
            }
        }
        """;

        var success = AnthropicUsageExtractor.TryExtract(json, out var usage);

        success.Should().BeTrue();
        // Invariant: InputTokens includes CacheTokens + creation tokens + input_tokens = 50 + 20 + 10 = 80
        usage.InputTokens.Should().Be(80);
        usage.OutputTokens.Should().Be(15);
        usage.CacheTokens.Should().Be(20);
        usage.TotalTokens.Should().Be(95);
    }

    [Fact]
    public void AnthropicUsageExtractor_RawUsage_PrefersRaw()
    {
        var json = """
        {
            "id": "msg_123",
            "usage": {
                "input_tokens": 2050,
                "output_tokens": 15,
                "_orr_raw": {
                    "input_tokens": 50,
                    "output_tokens": 15,
                    "cache_read_input_tokens": 20
                }
            }
        }
        """;

        var success = AnthropicUsageExtractor.TryExtract(json, out var usage);

        success.Should().BeTrue();
        usage.InputTokens.Should().Be(70);
        usage.OutputTokens.Should().Be(15);
        usage.CacheTokens.Should().Be(20);
    }

    [Fact]
    public async Task AnthropicStreamSession_PreservesEventsAndAccumulatesUsageAsync()
    {
        var ssePayload = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-3-7-sonnet-20250219","usage":{"input_tokens":100,"output_tokens":1,"cache_read_input_tokens":30,"cache_creation_input_tokens":0}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello world"}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":25}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
        };

        var session = new AnthropicStreamSession(httpResponse);
        using var destStream = new MemoryStream();

        var usage = await session.CopyToAsync(destStream, CancellationToken.None);

        destStream.Position = 0;
        using var reader = new StreamReader(destStream, Encoding.UTF8);
        var streamedContent = await reader.ReadToEndAsync();

        streamedContent.Should().Contain("event: message_start");
        streamedContent.Should().Contain("event: content_block_delta");
        streamedContent.Should().Contain("Hello world");

        // Usage check: inputTokens = 100 + 30 = 130, outputTokens = 25, cacheTokens = 30
        usage.InputTokens.Should().Be(130);
        usage.OutputTokens.Should().Be(25);
        usage.CacheTokens.Should().Be(30);
        usage.TotalTokens.Should().Be(155);
    }
}
