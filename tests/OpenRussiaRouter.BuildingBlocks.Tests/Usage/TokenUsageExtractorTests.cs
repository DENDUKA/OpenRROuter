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
