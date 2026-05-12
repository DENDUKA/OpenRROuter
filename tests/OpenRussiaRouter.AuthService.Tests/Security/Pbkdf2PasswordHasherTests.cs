using OpenRussiaRouter.AuthService.Security;

namespace OpenRussiaRouter.AuthService.Tests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    [Fact]
    public void HashPassword_ValidPassword_DoesNotStoreRawPassword()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var hash = hasher.HashPassword("StrongPassword123!");

        Assert.NotEqual("StrongPassword123!", hash);
        Assert.StartsWith("pbkdf2-sha256.", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.HashPassword("StrongPassword123!");

        var result = hasher.VerifyPassword("StrongPassword123!", hash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.HashPassword("StrongPassword123!");

        var result = hasher.VerifyPassword("WrongPassword123!", hash);

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_ShortPassword_Throws()
    {
        var hasher = new Pbkdf2PasswordHasher();

        Assert.Throws<ArgumentException>(() => hasher.HashPassword("short"));
    }
}

