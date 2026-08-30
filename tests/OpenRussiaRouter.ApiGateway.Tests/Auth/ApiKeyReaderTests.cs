using FluentAssertions;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.Auth;

namespace OpenRussiaRouter.ApiGateway.Tests.Auth;

public sealed class ApiKeyReaderTests
{
    [Fact]
    public void TryRead_OnlyBearer_ReturnsSuccess()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer sk-or-test-bearer";

        var status = ApiKeyReader.TryRead(context.Request, out var token);

        status.Should().Be(ApiKeyReadStatus.Success);
        token.Should().Be("sk-or-test-bearer");
    }

    [Fact]
    public void TryRead_OnlyXApiKey_ReturnsSuccess()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-api-key"] = "sk-or-test-x-api-key";

        var status = ApiKeyReader.TryRead(context.Request, out var token);

        status.Should().Be(ApiKeyReadStatus.Success);
        token.Should().Be("sk-or-test-x-api-key");
    }

    [Fact]
    public void TryRead_BothMatching_ReturnsSuccess()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer sk-or-same-key";
        context.Request.Headers["x-api-key"] = "sk-or-same-key";

        var status = ApiKeyReader.TryRead(context.Request, out var token);

        status.Should().Be(ApiKeyReadStatus.Success);
        token.Should().Be("sk-or-same-key");
    }

    [Fact]
    public void TryRead_BothConflicting_ReturnsConflict()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer sk-or-key-1";
        context.Request.Headers["x-api-key"] = "sk-or-key-2";

        var status = ApiKeyReader.TryRead(context.Request, out var token);

        status.Should().Be(ApiKeyReadStatus.Conflict);
        token.Should().BeEmpty();
    }

    [Fact]
    public void TryRead_NoAuthHeaders_ReturnsNone()
    {
        var context = new DefaultHttpContext();

        var status = ApiKeyReader.TryRead(context.Request, out var token);

        status.Should().Be(ApiKeyReadStatus.None);
        token.Should().BeEmpty();
    }
}
