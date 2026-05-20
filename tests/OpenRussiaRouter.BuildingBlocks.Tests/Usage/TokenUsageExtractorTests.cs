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

        Assert.True(result);
        Assert.Equal(11, usage.InputTokens);
        Assert.Equal(22, usage.OutputTokens);
        Assert.Equal(5, usage.CacheTokens);
    }

    [Fact]
    public void TryExtractStreamingSse_MissingUsage_ReturnsFalse()
    {
        var body = """
            data: {"choices":[{"delta":{"content":"hello"}}]}

            data: [DONE]

            """;

        var result = TokenUsageExtractor.TryExtractStreamingSse(body, out var usage);

        Assert.False(result);
        Assert.Equal(default, usage);
    }
}
