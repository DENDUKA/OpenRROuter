using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.Seo;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class CanonicalUrlNormalizerTests
{
    [Theory]
    [InlineData("/models/", "/models")]
    [InlineData("/Models", "/models")]
    [InlineData("/MODELS/", "/models")]
    [InlineData("/Providers", "/providers")]
    [InlineData("/legal/Privacy", "/legal/privacy")]
    [InlineData("/support/", "/support")]
    public void TryNormalize_DuplicateFormOfStaticPage_ReturnsCanonicalForm(string path, string expected) =>
        CanonicalUrlNormalizer.TryNormalize(path).Should().Be(expected);

    [Theory]
    [InlineData("/")]
    [InlineData("/models")]
    [InlineData("/providers")]
    [InlineData("/legal/privacy")]
    public void TryNormalize_AlreadyCanonical_ReturnsNull(string path) =>
        CanonicalUrlNormalizer.TryNormalize(path).Should().BeNull();

    [Theory]
    [InlineData("/models/xiaomi/MiMo-V2.5-Pro")]
    [InlineData("/providers/logo/Z.ai")]
    [InlineData("/tools/image/A1B2C3")]
    public void TryNormalize_PathCarryingRouteValues_PreservesCase(string path) =>
        // Lower-casing these would corrupt ids compared ordinally downstream. Model detail URLs
        // canonicalize themselves through ModelDetailRoute instead.
        CanonicalUrlNormalizer.TryNormalize(path).Should().BeNull();

    [Fact]
    public void TryNormalize_TrailingSlashOnRouteValuePath_TrimsSlashButKeepsCase() =>
        CanonicalUrlNormalizer.TryNormalize("/models/xiaomi/MiMo-V2.5-Pro/")
            .Should().Be("/models/xiaomi/MiMo-V2.5-Pro");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    public void TryNormalize_DegenerateInput_ReturnsNull(string? path) =>
        // "//" has no meaningful canonical form here — routing rejects it on its own.
        CanonicalUrlNormalizer.TryNormalize(path).Should().BeNull();
}
