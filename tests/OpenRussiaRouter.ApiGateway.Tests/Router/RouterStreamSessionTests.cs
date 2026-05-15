using System.Net;
using System.Text;
using OpenRussiaRouter.ApiGateway.Router;

namespace OpenRussiaRouter.ApiGateway.Tests.Router;

public sealed class RouterStreamSessionTests
{
    [Fact]
    public void IsSuccess_SuccessResponse_ReturnsTrue()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var session = new RouterStreamSession(response);
        Assert.True(session.IsSuccess);
    }

    [Fact]
    public void IsSuccess_ServiceUnavailable_ReturnsFalse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var session = new RouterStreamSession(response);
        Assert.False(session.IsSuccess);
    }

    [Fact]
    public void StatusCode_ReflectsHttpResponseStatus()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var session = new RouterStreamSession(response);
        Assert.Equal(429, session.StatusCode);
    }

    [Fact]
    public async Task CopyToAsync_ForwardsChunksAndDoneMarker()
    {
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
            "data: [DONE]\n\n";

        await using var session = CreateSession(sseBody);
        var destination = new MemoryStream();

        await session.CopyToAsync(destination, CancellationToken.None);

        destination.Position = 0;
        var output = new StreamReader(destination).ReadToEnd();
        Assert.Contains("data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}", output);
        Assert.Contains("data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}", output);
        Assert.Contains("data: [DONE]", output);
    }

    [Fact]
    public async Task CopyToAsync_ExtractsUsageFromFinalChunk_OpenAiFieldNames()
    {
        var usageChunk = """{"usage":{"prompt_tokens":10,"completion_tokens":20}}""";
        var sseBody = $"data: {usageChunk}\n\ndata: [DONE]\n\n";

        await using var session = CreateSession(sseBody);
        var usage = await session.CopyToAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
    }

    [Fact]
    public async Task CopyToAsync_ExtractsUsageFromFinalChunk_AnthropicFieldNames()
    {
        var usageChunk = """{"usage":{"input_tokens":15,"output_tokens":30}}""";
        var sseBody = $"data: {usageChunk}\n\ndata: [DONE]\n\n";

        await using var session = CreateSession(sseBody);
        var usage = await session.CopyToAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal(15, usage.InputTokens);
        Assert.Equal(30, usage.OutputTokens);
    }

    [Fact]
    public async Task CopyToAsync_NoUsageInStream_ReturnsDefaultUsage()
    {
        var sseBody = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\ndata: [DONE]\n\n";

        await using var session = CreateSession(sseBody);
        var usage = await session.CopyToAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
    }

    [Fact]
    public async Task CopyToAsync_SkipsNonDataLines()
    {
        var sseBody =
            ": keep-alive\n\n" +
            "event: delta\n\n" +
            "data: {\"choices\":[]}\n\n" +
            "data: [DONE]\n\n";

        await using var session = CreateSession(sseBody);
        var destination = new MemoryStream();

        await session.CopyToAsync(destination, CancellationToken.None);

        destination.Position = 0;
        var output = new StreamReader(destination).ReadToEnd();
        Assert.DoesNotContain(": keep-alive", output);
        Assert.DoesNotContain("event: delta", output);
        Assert.Contains("data: {\"choices\":[]}", output);
    }

    private static RouterStreamSession CreateSession(string sseBody)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
        };
        return new RouterStreamSession(response);
    }
}
