using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.Catalog;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class ModelDetailRouteTests
{
    [Theory]
    [InlineData("claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("ORR/Gemini-3.1-Pro", "Gemini-3.1-Pro")]
    [InlineData("a/b/c", "c")]
    [InlineData("", "")]
    public void SlugOf_TakesEverythingAfterTheLastSlash(string modelId, string expected) =>
        ModelDetailRoute.SlugOf(modelId).Should().Be(expected);

    [Fact]
    public void ProviderSegmentOf_PrefersProviderId()
    {
        ModelDetailRoute.ProviderSegmentOf("ORR/Gemini-3.1-Pro", "google", "Google")
            .Should().Be("google");
    }

    [Fact]
    public void ProviderSegmentOf_FallsBackToIdPrefixThenDisplayName()
    {
        // Без provider id префикс единственного слэша ещё даёт рабочий сегмент.
        ModelDetailRoute.ProviderSegmentOf("xiaomi/mimo-v2.5-pro", null, null).Should().Be("xiaomi");
        // Ни id, ни префикса — остаётся отображаемое имя.
        ModelDetailRoute.ProviderSegmentOf("claude-opus-4-8", null, "Anthropic").Should().Be("Anthropic");
        // Совсем нечего подставить — модель не адресуема.
        ModelDetailRoute.ProviderSegmentOf("claude-opus-4-8", null, null).Should().BeNull();
    }

    [Theory]
    [InlineData("claude-opus-4-8", "anthropic", "Anthropic", "/models/anthropic/claude-opus-4-8")]
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "/models/google/Gemini-3.1-Pro")]
    [InlineData("GPT-5.5", "openai", "OpenAI", "/models/openai/GPT-5.5")]
    public void TryBuildPath_BuildsProviderIdPlusSlug(
        string modelId, string providerId, string providerName, string expected) =>
        ModelDetailRoute.TryBuildPath(modelId, providerId, providerName).Should().Be(expected);

    [Fact]
    public void TryBuildPath_ReturnsNull_WhenModelIsNotAddressable()
    {
        ModelDetailRoute.TryBuildPath("", "anthropic", "Anthropic").Should().BeNull();
        ModelDetailRoute.TryBuildPath("claude-opus-4-8", null, null).Should().BeNull();
    }

    [Fact]
    public void IsCanonical_OnlyForTheExactProviderIdAndSlug()
    {
        ModelDetailRoute.IsCanonical("ORR/Gemini-3.1-Pro", "google", "Google", "google", "Gemini-3.1-Pro")
            .Should().BeTrue();
        // Отображаемое имя провайдера, префикс id и другой регистр — не канонический URL.
        ModelDetailRoute.IsCanonical("ORR/Gemini-3.1-Pro", "google", "Google", "Google", "Gemini-3.1-Pro")
            .Should().BeFalse();
        ModelDetailRoute.IsCanonical("ORR/Gemini-3.1-Pro", "google", "Google", "ORR", "Gemini-3.1-Pro")
            .Should().BeFalse();
        ModelDetailRoute.IsCanonical("ORR/Gemini-3.1-Pro", "google", "Google", "google", "ORR/Gemini-3.1-Pro")
            .Should().BeFalse();
    }

    [Theory]
    // Канонический URL.
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "google", "Gemini-3.1-Pro", true)]
    // Старые ссылки: отображаемое имя провайдера, префикс id, полный id в слаге (в т.ч. с %2F).
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "Google", "Gemini-3.1-Pro", true)]
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "ORR", "Gemini-3.1-Pro", true)]
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "Google", "ORR/Gemini-3.1-Pro", true)]
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "Google", "ORR%2FGemini-3.1-Pro", true)]
    // Чужой провайдер или чужой слаг — не эта модель.
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "openai", "Gemini-3.1-Pro", false)]
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "google", "GPT-5.5", false)]
    [InlineData("ORR/Gemini-3.1-Pro", "google", "Google", "", "Gemini-3.1-Pro", false)]
    public void Matches_AcceptsCanonicalAndLegacySegments(
        string modelId, string providerId, string providerName, string provider, string slug, bool expected) =>
        ModelDetailRoute.Matches(modelId, providerId, providerName, provider, slug)
            .Should().Be(expected);

    [Fact]
    public void Matches_AcceptsUpstreamModelIdMatching()
    {
        ModelDetailRoute.Matches(
            modelId: "DeepSeek-V4-Flash-Raw",
            providerId: "deepseek",
            providerDisplayName: "DeepSeek",
            providerSegment: "DeepSeek",
            slugSegment: "deepseek-v4-flash",
            upstreamModelId: "deepseek/deepseek-v4-flash"
        ).Should().BeTrue();
    }
}
