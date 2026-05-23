using System.Text;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.OpenAi;

namespace OpenRussiaRouter.ApiGateway.Tests.OpenAi;

public sealed class ChatCompletionRequestReaderTests
{
    [Fact]
    public async Task ReadAsync_ModelAndStreamPresent_ReturnsMetadata()
    {
        await using var body = ToStream("""{"model":"gpt-test","stream":true,"messages":[{"role":"user","content":"secret"}]}""");

        var result = await ChatCompletionRequestReader.ReadAsync(body, CancellationToken.None);

        result.Model.Should().Be("gpt-test");
        result.Stream.Should().BeTrue();
    }

    [Fact]
    public async Task ReadBodyAsync_ReturnsRawJsonAndMetadata()
    {
        const string json = """{"model":"gpt-test","stream":false}""";
        await using var body = ToStream(json);

        var result = await ChatCompletionRequestReader.ReadBodyAsync(body, CancellationToken.None);

        result.Json.Should().Be(json);
        result.Metadata.Model.Should().Be("gpt-test");
        result.Metadata.Stream.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ModelMissing_ReturnsNullModel()
    {
        await using var body = ToStream("""{"messages":[]}""");

        var result = await ChatCompletionRequestReader.ReadAsync(body, CancellationToken.None);

        result.Model.Should().BeNull();
        result.Stream.Should().BeFalse();
    }

    private static MemoryStream ToStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
