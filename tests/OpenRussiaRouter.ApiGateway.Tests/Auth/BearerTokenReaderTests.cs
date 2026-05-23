using FluentAssertions;
using OpenRussiaRouter.ApiGateway.Auth;

namespace OpenRussiaRouter.ApiGateway.Tests.Auth;

public sealed class BearerTokenReaderTests
{
    [Theory]
    [InlineData("Bearer orr_test", true, "orr_test")]
    [InlineData("bearer orr_test", true, "orr_test")]
    [InlineData("Bearer   orr_test  ", true, "orr_test")]
    [InlineData("Basic orr_test", false, "")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void TryRead_Header_ReturnsExpectedResult(string? header, bool expectedResult, string expectedToken)
    {
        var result = BearerTokenReader.TryRead(header, out var token);

        result.Should().Be(expectedResult);
        token.Should().Be(expectedToken);
    }
}
