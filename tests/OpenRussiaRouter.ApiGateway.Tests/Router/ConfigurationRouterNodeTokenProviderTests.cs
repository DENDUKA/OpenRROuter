using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.ApiGateway.Router;

namespace OpenRussiaRouter.ApiGateway.Tests.Router;

public sealed class ConfigurationRouterNodeTokenProviderTests
{
    [Fact]
    public void Resolve_ConfiguredToken_ReturnsToken()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RouterNodeTokens:node-a"] = "secret-token"
            })
            .Build();
        var provider = new ConfigurationRouterNodeTokenProvider(configuration);

        var result = provider.Resolve("node-a");

        Assert.Equal("secret-token", result);
    }

    [Fact]
    public void Resolve_MissingToken_ReturnsEmpty()
    {
        var provider = new ConfigurationRouterNodeTokenProvider(new ConfigurationBuilder().Build());

        var result = provider.Resolve("node-a");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Resolve_EmptySecretName_ReturnsEmpty()
    {
        var provider = new ConfigurationRouterNodeTokenProvider(new ConfigurationBuilder().Build());

        Assert.Equal(string.Empty, provider.Resolve(""));
        Assert.Equal(string.Empty, provider.Resolve("   "));
    }
}

