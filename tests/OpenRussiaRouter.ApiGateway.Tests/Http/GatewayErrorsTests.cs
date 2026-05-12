using System.Text.Json;
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
        Assert.Equal(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);
        Assert.Equal("billing_error", error.GetProperty("type").GetString());
        Assert.Equal(ErrorCodes.InsufficientBalance, error.GetProperty("code").GetString());
        Assert.Equal("Insufficient balance.", error.GetProperty("message").GetString());
        Assert.Equal("corr-1", error.GetProperty("request_id").GetString());
        Assert.Equal("corr-1", context.Response.Headers[CorrelationIds.HeaderName]);
        Assert.Equal("corr-1", context.Response.Headers["X-Request-Id"]);
    }

    [Fact]
    public async Task Internal_DoesNotLeakExceptionDetails()
    {
        var context = CreateContext("corr-2");

        await GatewayErrors.Internal(context.Request).ExecuteAsync(context);

        var error = (await ReadBodyAsync(context)).RootElement.GetProperty("error");
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("internal_error", error.GetProperty("type").GetString());
        Assert.Equal(ErrorCodes.InternalError, error.GetProperty("code").GetString());
        Assert.Equal("Internal server error.", error.GetProperty("message").GetString());
        Assert.DoesNotContain("Exception", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
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
