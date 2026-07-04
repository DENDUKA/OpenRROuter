using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    [Fact]
    public async Task Authentication_Returns401WithCorrectType()
    {
        var context = CreateContext("corr-3");

        await GatewayErrors.Authentication(context.Request, ErrorCodes.AuthenticationRequired, "Bearer token required")
            .ExecuteAsync(context);

        var error = (await ReadBodyAsync(context)).RootElement.GetProperty("error");
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        error.GetProperty("type").GetString().Should().Be("authentication_error");
        error.GetProperty("code").GetString().Should().Be(ErrorCodes.AuthenticationRequired);
    }

    [Fact]
    public async Task ServiceUnavailable_Returns503WithCorrectType()
    {
        var context = CreateContext("corr-4");

        await GatewayErrors.ServiceUnavailable(context.Request, ErrorCodes.RouterUnavailable, "No router available")
            .ExecuteAsync(context);

        var error = (await ReadBodyAsync(context)).RootElement.GetProperty("error");
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        error.GetProperty("type").GetString().Should().Be("service_unavailable");
        error.GetProperty("code").GetString().Should().Be(ErrorCodes.RouterUnavailable);
    }

    [Fact]
    public async Task Validation_Returns400WithCorrectType()
    {
        var context = CreateContext("corr-5");

        await GatewayErrors.Validation(context.Request, ErrorCodes.ValidationFailed, "model is required")
            .ExecuteAsync(context);

        var error = (await ReadBodyAsync(context)).RootElement.GetProperty("error");
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        error.GetProperty("type").GetString().Should().Be("invalid_request_error");
        error.GetProperty("code").GetString().Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task WriteAsync_UnknownType_Returns500()
    {
        var context = CreateContext("corr-7");

        await GatewayErrors.WriteAsync(context, "unknown_type", "unknown_code", "test", "corr-7");

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var error = (await ReadBodyAsync(context)).RootElement.GetProperty("error");
        error.GetProperty("type").GetString().Should().Be("unknown_type");
    }

    [Fact]
    public async Task WriteAsync_HasStarted_DoesNotThrow()
    {
        var context = CreateContext("corr-8");
        context.Features.Set<IHttpResponseFeature>(new FakeStartedResponseFeature());

        await GatewayErrors.WriteAsync(context, "internal_error", "test_code", "test message", "corr-8");

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
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

    private sealed class FakeStartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public string? ReasonPhrase { get; set; }
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
