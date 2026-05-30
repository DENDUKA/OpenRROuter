using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Usage;

public sealed class TokenUsageExtractorTests
{
    [Fact]
    public void TryExtractStreamingSse_FinalUsageChunk_ReturnsUsage()
    {
        var body = """
            data: {"choices":[{"delta":{"content":"hello"}}]}

            data: {"usage":{"prompt_tokens":11,"completion_tokens":22,"prompt_tokens_details":{"cached_tokens":5}}}

            data: [DONE]

            """;

        var result = TokenUsageExtractor.TryExtractStreamingSse(body, out var usage);

        result.Should().BeTrue();
        usage.InputTokens.Should().Be(11);
        usage.OutputTokens.Should().Be(22);
        usage.CacheTokens.Should().Be(5);
    }

    [Fact]
    public void TryExtractStreamingSse_PrefersOrrRawOverBufferedUsage()
    {
        var body = """
            data: {"choices":[{"delta":{"content":"hi"}}]}

            data: {"usage":{"prompt_tokens":2144,"completion_tokens":2042,"total_tokens":4186,"_orr_raw":{"prompt_tokens":144,"completion_tokens":42}}}

            data: [DONE]

            """;

        var result = TokenUsageExtractor.TryExtractStreamingSse(body, out var usage);

        result.Should().BeTrue();
        usage.InputTokens.Should().Be(144);
        usage.OutputTokens.Should().Be(42);
    }

    [Fact]
    public void TryExtractJson_OpenAiCachedTokens_AreSubsetOfPromptTokens()
    {
        // OpenAI: prompt_tokens already includes cached_tokens, so input is unchanged.
        var json = """
            {"usage":{"prompt_tokens":1000,"completion_tokens":50,"prompt_tokens_details":{"cached_tokens":800}}}
            """;

        var result = TokenUsageExtractor.TryExtractJson(json, out var usage);

        result.Should().BeTrue();
        usage.InputTokens.Should().Be(1000);
        usage.OutputTokens.Should().Be(50);
        usage.CacheTokens.Should().Be(800);
    }

    [Fact]
    public void TryExtractJson_AnthropicCacheReadTokens_AreAddedToInput()
    {
        // Anthropic: input_tokens excludes cache reads, so cache is folded into input
        // to keep the invariant "InputTokens includes CacheTokens".
        var json = """
            {"usage":{"input_tokens":200,"output_tokens":50,"cache_read_input_tokens":800}}
            """;

        var result = TokenUsageExtractor.TryExtractJson(json, out var usage);

        result.Should().BeTrue();
        usage.InputTokens.Should().Be(1000);
        usage.OutputTokens.Should().Be(50);
        usage.CacheTokens.Should().Be(800);
    }

    [Fact]
    public void TryExtractStreamingSse_MissingUsage_ReturnsFalse()
    {
        var body = """
            data: {"choices":[{"delta":{"content":"hello"}}]}

            data: [DONE]

            """;

        var result = TokenUsageExtractor.TryExtractStreamingSse(body, out var usage);

        result.Should().BeFalse();
        usage.Should().Be(default(TokenUsage));
    }
}
