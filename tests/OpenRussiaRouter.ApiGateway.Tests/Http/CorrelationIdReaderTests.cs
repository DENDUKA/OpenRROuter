using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Tests.Http;

public sealed class CorrelationIdReaderTests
{
    [Fact]
    public void Get_HeaderPresent_ReturnsHeaderValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIds.HeaderName] = "corr-1";

        var result = CorrelationIdReader.Get(context.Request);

        Assert.Equal("corr-1", result);
    }

    [Fact]
    public void Get_HeaderMissing_ReturnsGeneratedValue()
    {
        var context = new DefaultHttpContext();

        var result = CorrelationIdReader.Get(context.Request);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}

