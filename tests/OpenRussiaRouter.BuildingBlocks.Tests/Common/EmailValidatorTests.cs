using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Common;

public sealed class EmailValidatorTests
{
    [Fact]
    public void Normalize_ValidEmail_ReturnsLowercaseTrimmed()
    {
        var result = EmailValidator.Normalize("  User@Example.COM  ");

        result.Should().Be("user@example.com");
    }

    [Fact]
    public void Normalize_Null_ThrowsArgumentException()
    {
        var act = () => EmailValidator.Normalize(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_EmptyString_ThrowsArgumentException()
    {
        var act = () => EmailValidator.Normalize("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ThrowsArgumentException()
    {
        var act = () => EmailValidator.Normalize("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_MissingAtSign_ThrowsArgumentException()
    {
        var act = () => EmailValidator.Normalize("userexample.com");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*@*");
    }
}
