using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Repository;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class InMemoryAuthApiKeyValidatorTests
{
    [Fact]
    public async Task Validate_RegisteredRawKey_ReturnsUserAndKeyContext()
    {
        var validator = CreateValidator();
        validator.RegisterRawKey("orr_test_key", "user-1", "key-1");

        var result = await validator.ValidateAsync("orr_test_key");

        result.IsValid.Should().BeTrue();
        result.UserId.Should().Be("user-1");
        result.ApiKeyId.Should().Be("key-1");
        result.ApiKeyPrefix.Should().Be("orr_test_key");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Validate_UnknownRawKey_ReturnsInvalidApiKey()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("orr_unknown");

        result.IsValid.Should().BeFalse();
        result.UserId.Should().BeNull();
        result.ApiKeyId.Should().BeNull();
        result.ApiKeyPrefix.Should().BeNull();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidApiKey);
    }

    [Fact]
    public async Task Validate_ConfigSeededRawKey_ReturnsUserAndKeyContext()
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

        var result = await validator.ValidateAsync("orr_seed_key");

        result.IsValid.Should().BeTrue();
        result.UserId.Should().Be("user-seed");
        result.ApiKeyId.Should().Be("key-seed");
    }

    [Fact]
    public async Task Validate_SeedKey_ReturnsPrefixSnapshot()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SeedApiKeys:0:RawKey"] = "sk-or-seeded-secret",
                ["Auth:SeedApiKeys:0:UserId"] = "user-seed",
                ["Auth:SeedApiKeys:0:ApiKeyId"] = "key-seed"
            })
            .Build();
        var validator = new InMemoryAuthApiKeyValidator(new Sha256SecretHasher(), configuration);

        var result = await validator.ValidateAsync("sk-or-seeded-secret");

        result.IsValid.Should().BeTrue();
        result.ApiKeyPrefix.Should().Be("sk-or-seeded");
    }

    private static InMemoryAuthApiKeyValidator CreateValidator()
    {
        return new InMemoryAuthApiKeyValidator(
            new Sha256SecretHasher(),
            new ConfigurationBuilder().Build());
    }
}
