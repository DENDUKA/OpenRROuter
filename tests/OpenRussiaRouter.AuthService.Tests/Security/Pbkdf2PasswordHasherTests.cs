using FluentAssertions;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    [Fact]
    public void HashPassword_ValidPassword_DoesNotStoreRawPassword()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var hash = hasher.HashPassword("StrongPassword123!");

        hash.Should().NotBe("StrongPassword123!");
        hash.Should().StartWith("pbkdf2-sha256.");
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.HashPassword("StrongPassword123!");

        var result = hasher.VerifyPassword("StrongPassword123!", hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.HashPassword("StrongPassword123!");

        var result = hasher.VerifyPassword("WrongPassword123!", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void HashPassword_ShortPassword_Throws()
    {
        var hasher = new Pbkdf2PasswordHasher();

        ((Action)(() => hasher.HashPassword("short"))).Should().Throw<ArgumentException>();
    }
}
