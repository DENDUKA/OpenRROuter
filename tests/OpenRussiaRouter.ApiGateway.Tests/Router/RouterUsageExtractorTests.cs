using OpenRussiaRouter.ApiGateway.Router;

namespace OpenRussiaRouter.ApiGateway.Tests.Router;

public sealed class RouterUsageExtractorTests
{
    [Fact]
    public void TryExtract_9RouterUsageShape_ReturnsUsage()
    {
        var json = """{"usage":{"input_tokens":10,"output_tokens":20,"cache_tokens":3}}""";

        var result = RouterUsageExtractor.TryExtract(json, out var usage);

        Assert.True(result);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(3, usage.CacheTokens);
    }

    [Fact]
    public void TryExtract_OpenAiUsageShape_ReturnsUsage()
    {
        var json = """{"usage":{"prompt_tokens":10,"completion_tokens":20,"cached_tokens":3}}""";

        var result = RouterUsageExtractor.TryExtract(json, out var usage);

        Assert.True(result);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(3, usage.CacheTokens);
    }

    [Fact]
    public void TryExtract_MissingUsage_ReturnsFalse()
    {
        var result = RouterUsageExtractor.TryExtract("""{"id":"chatcmpl_1"}""", out _);

        Assert.False(result);
    }

    [Fact]
    public void TryExtract_NestedPromptDetailsCache_ReturnsCacheTokens()
    {
        var json = """{"usage":{"prompt_tokens":10,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":7}}}""";

        var result = RouterUsageExtractor.TryExtract(json, out var usage);

        Assert.True(result);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(7, usage.CacheTokens);
    }

    [Fact]
    public void TryExtract_InputTokenDetailsCache_ReturnsCacheTokens()
    {
        var json = """{"usage":{"input_tokens":10,"output_tokens":20,"input_tokens_details":{"cached_tokens":8}}}""";

        var result = RouterUsageExtractor.TryExtract(json, out var usage);

        Assert.True(result);
        Assert.Equal(8, usage.CacheTokens);
    }

    [Fact]
    public void TryExtract_MalformedJson_ReturnsFalse()
    {
        var result = RouterUsageExtractor.TryExtract("""{"usage":""", out var usage);

        Assert.False(result);
        Assert.Equal(default, usage);
    }
}
