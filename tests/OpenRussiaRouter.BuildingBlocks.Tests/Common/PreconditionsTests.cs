using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Common;

public sealed class PreconditionsTests
{
    [Fact]
    public void RequireValue_ValidValue_ReturnsTrimmed()
    {
        var result = Preconditions.RequireValue("  hello  ", "test");

        result.Should().Be("hello");
    }

    [Fact]
    public void RequireValue_Null_ThrowsArgumentException()
    {
        var act = () => Preconditions.RequireValue(null!, "test");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireValue_EmptyString_ThrowsArgumentException()
    {
        var act = () => Preconditions.RequireValue("", "test");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireValue_WhitespaceOnly_ThrowsArgumentException()
    {
        var act = () => Preconditions.RequireValue("   ", "test");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireNotNull_NullValue_ThrowsArgumentNullException()
    {
        var act = () => Preconditions.RequireNotNull<string>(null!, "test");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RequireNotNull_NonNullValue_ReturnsValue()
    {
        var result = Preconditions.RequireNotNull("hello", "test");

        result.Should().Be("hello");
    }
}
