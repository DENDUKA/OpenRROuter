using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.Catalog;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class ModelDetailRouteTests
{
    [Theory]
    [InlineData("claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("RWT/Gemini-3.1-Pro", "Gemini-3.1-Pro")]
    [InlineData("a/b/c", "c")]
    [InlineData("", "")]
    public void SlugOf_TakesEverythingAfterTheLastSlash(string modelId, string expected) =>
        ModelDetailRoute.SlugOf(modelId).Should().Be(expected);

    [Fact]
    public void ProviderSegmentOf_PrefersProviderId()
    {
        ModelDetailRoute.ProviderSegmentOf("RWT/Gemini-3.1-Pro", "google", "Google")
            .Should().Be("google");
    }

    [Fact]
    public void ProviderSegmentOf_FallsBackToIdPrefixThenDisplayName()
    {
        // Без provider id префикс единственного слэша ещё даёт рабочий сегмент.
        ModelDetailRoute.ProviderSegmentOf("xiaomi/mimo-v2.5-pro", null, null).Should().Be("xiaomi");
        // Ни id, ни префикса — остаётся отображаемое имя.
        ModelDetailRoute.ProviderSegmentOf("claude-opus-4-8", null, "Antropic").Should().Be("Antropic");
        // Совсем нечего подставить — модель не адресуема.
        ModelDetailRoute.ProviderSegmentOf("claude-opus-4-8", null, null).Should().BeNull();
    }

    [Theory]
    [InlineData("claude-opus-4-8", "antropic", "Antropic", "/models/antropic/claude-opus-4-8")]
    [InlineData("RWT/Gemini-3.1-Pro", "google", "Google", "/models/google/Gemini-3.1-Pro")]
    [InlineData("GPT-5.5", "openai", "OpenAI", "/models/openai/GPT-5.5")]
    public void TryBuildPath_BuildsProviderIdPlusSlug(
        string modelId, string providerId, string providerName, string expected) =>
        ModelDetailRoute.TryBuildPath(modelId, providerId, providerName).Should().Be(expected);

    [Fact]
    public void TryBuildPath_ReturnsNull_WhenModelIsNotAddressable()
    {
        ModelDetailRoute.TryBuildPath("", "antropic", "Antropic").Should().BeNull();
        ModelDetailRoute.TryBuildPath("claude-opus-4-8", null, null).Should().BeNull();
    }

    [Fact]
    public void IsCanonical_OnlyForTheExactProviderIdAndSlug()
    {
        ModelDetailRoute.IsCanonical("RWT/Gemini-3.1-Pro", "google", "Google", "google", "Gemini-3.1-Pro")
            .Should().BeTrue();
        // Отображаемое имя провайдера, префикс id и другой регистр — не канонический URL.
        ModelDetailRoute.IsCanonical("RWT/Gemini-3.1-Pro", "google", "Google", "Google", "Gemini-3.1-Pro")
            .Should().BeFalse();
        ModelDetailRoute.IsCanonical("RWT/Gemini-3.1-Pro", "google", "Google", "ORR", "Gemini-3.1-Pro")
            .Should().BeFalse();
        ModelDetailRoute.IsCanonical("RWT/Gemini-3.1-Pro", "google", "Google", "google", "RWT/Gemini-3.1-Pro")
            .Should().BeFalse();
    }

    [Theory]
    // Канонический URL.
    [InlineData("google", "Gemini-3.1-Pro", true)]
    // Старые ссылки: отображаемое имя провайдера, префикс id, полный id в слаге (в т.ч. с %2F).
    [InlineData("Google", "Gemini-3.1-Pro", true)]
    [InlineData("ORR", "Gemini-3.1-Pro", true)]
    [InlineData("Google", "RWT/Gemini-3.1-Pro", true)]
    [InlineData("Google", "ORR%2FGemini-3.1-Pro", true)]
    // Чужой провайдер или чужой слаг — не эта модель.
    [InlineData("openai", "Gemini-3.1-Pro", false)]
    [InlineData("google", "GPT-5.5", false)]
    [InlineData("", "Gemini-3.1-Pro", false)]
    public void Matches_AcceptsCanonicalAndLegacySegments(string provider, string slug, bool expected) =>
        ModelDetailRoute.Matches("RWT/Gemini-3.1-Pro", "google", "Google", provider, slug)
            .Should().Be(expected);
}
