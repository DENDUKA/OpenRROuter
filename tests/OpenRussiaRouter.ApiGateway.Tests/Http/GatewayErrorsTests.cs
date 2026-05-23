using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Tests.Http;

public sealed class GatewayErrorsTests
{
    [Fact]
    public async Task Billing_ReturnsOpenAiCompatibleErrorAndRequestHeaders()
    {
        var context = CreateContext("corr-1");

        await GatewayErrors.Billing(context.Request, ErrorCodes.InsufficientBalance, "Insufficient balance.")
            .ExecuteAsync(context);

        var json = await ReadBodyAsync(context);
        var error = json.RootElement.GetProperty("error");
        context.Response.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        error.GetProperty("type").GetString().Should().Be("billing_error");
        error.GetProperty("code").GetString().Should().Be(ErrorCodes.InsufficientBalance);
        error.GetProperty("message").GetString().Should().Be("Insufficient balance.");
        error.GetProperty("request_id").GetString().Should().Be("corr-1");
        context.Response.Headers[CorrelationIds.HeaderName].Should().Equal("corr-1");
        context.Response.Headers["X-Request-Id"].Should().Equal("corr-1");
    }

    [Fact]
    public async Task Internal_DoesNotLeakExceptionDetails()
    {
        var context = CreateContext("corr-2");

        await GatewayErrors.Internal(context.Request).ExecuteAsync(context);

        var error = (await ReadBodyAsync(context)).RootElement.GetProperty("error");
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.GetProperty("type").GetString().Should().Be("internal_error");
        error.GetProperty("code").GetString().Should().Be(ErrorCodes.InternalError);
        error.GetProperty("message").GetString().Should().Be("Internal server error.");
        error.GetProperty("message").GetString().Should().NotContainEquivalentOf("Exception");
    }

    private static DefaultHttpContext CreateContext(string correlationId)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        context.Request.Headers[CorrelationIds.HeaderName] = correlationId;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
