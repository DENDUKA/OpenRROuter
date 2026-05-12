using System.Text;
using OpenRussiaRouter.ApiGateway.OpenAi;

namespace OpenRussiaRouter.ApiGateway.Tests.OpenAi;

public sealed class ChatCompletionRequestReaderTests
{
    [Fact]
    public async Task ReadAsync_ModelAndStreamPresent_ReturnsMetadata()
    {
        await using var body = ToStream("""{"model":"gpt-test","stream":true,"messages":[{"role":"user","content":"secret"}]}""");

        var result = await ChatCompletionRequestReader.ReadAsync(body, CancellationToken.None);

        Assert.Equal("gpt-test", result.Model);
        Assert.True(result.Stream);
    }

    [Fact]
    public async Task ReadBodyAsync_ReturnsRawJsonAndMetadata()
    {
        const string json = """{"model":"gpt-test","stream":false}""";
        await using var body = ToStream(json);

        var result = await ChatCompletionRequestReader.ReadBodyAsync(body, CancellationToken.None);

        Assert.Equal(json, result.Json);
        Assert.Equal("gpt-test", result.Metadata.Model);
        Assert.False(result.Metadata.Stream);
    }

    [Fact]
    public async Task ReadAsync_ModelMissing_ReturnsNullModel()
    {
        await using var body = ToStream("""{"messages":[]}""");

        var result = await ChatCompletionRequestReader.ReadAsync(body, CancellationToken.None);

        Assert.Null(result.Model);
        Assert.False(result.Stream);
    }

    private static MemoryStream ToStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
