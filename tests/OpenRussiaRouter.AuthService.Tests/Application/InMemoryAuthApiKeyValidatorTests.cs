using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class InMemoryAuthApiKeyValidatorTests
{
    [Fact]
    public void Validate_RegisteredRawKey_ReturnsUserAndKeyContext()
    {
        var validator = CreateValidator();
        validator.RegisterRawKey("orr_test_key", "user-1", "key-1");

        var result = validator.Validate("orr_test_key");

        Assert.True(result.IsValid);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal("key-1", result.ApiKeyId);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Validate_UnknownRawKey_ReturnsInvalidApiKey()
    {
        var validator = CreateValidator();

        var result = validator.Validate("orr_unknown");

        Assert.False(result.IsValid);
        Assert.Null(result.UserId);
        Assert.Null(result.ApiKeyId);
        Assert.Equal(ErrorCodes.InvalidApiKey, result.ErrorCode);
    }

    [Fact]
    public void Validate_ConfigSeededRawKey_ReturnsUserAndKeyContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SeedApiKeys:0:RawKey"] = "orr_seed_key",
                ["Auth:SeedApiKeys:0:UserId"] = "user-seed",
                ["Auth:SeedApiKeys:0:ApiKeyId"] = "key-seed"
            })
            .Build();
        var validator = new InMemoryAuthApiKeyValidator(new Sha256SecretHasher(), configuration);

        var result = validator.Validate("orr_seed_key");

        Assert.True(result.IsValid);
        Assert.Equal("user-seed", result.UserId);
        Assert.Equal("key-seed", result.ApiKeyId);
    }

    private static InMemoryAuthApiKeyValidator CreateValidator()
    {
        return new InMemoryAuthApiKeyValidator(
            new Sha256SecretHasher(),
            new ConfigurationBuilder().Build());
    }
}

