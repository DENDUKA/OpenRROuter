using System.Text.Json;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.OpenAi;

namespace OpenRussiaRouter.ApiGateway.Tests.OpenAi;

public sealed class ReasoningRequestPatcherTests
{
    [Fact]
    public void TryNormalize_AllowedFlatEffort_ForwardsCanonicalValue()
    {
        var result = ReasoningRequestPatcher.TryNormalize(
            """{"reasoning_effort":" High "}""", true, ["low", "high"], "low");

        ReadEffort(result).Should().Be("high");
        result.Action.Should().Be(ReasoningPatchAction.Applied);
    }

    [Theory]
    [InlineData("""{"reasoning_effort":"xhigh"}""")]
    [InlineData("""{"reasoning":{"max_tokens":1000,"effort":"high"}}""")]
    [InlineData("""{"reasoning_effort":"low","reasoning":{"effort":"high"}}""")]
    [InlineData("""{"reasoning":{}}""")]
    public void TryNormalize_InvalidInput_AppliesDefault(string json)
    {
        var result = ReasoningRequestPatcher.TryNormalize(json, true, ["low", "medium", "high"], "medium");

        ReadEffort(result).Should().Be("medium");
        result.Action.Should().Be(ReasoningPatchAction.Defaulted);
    }

    [Fact]
    public void TryNormalize_MissingInput_AppliesConfiguredDefault()
    {
        var result = ReasoningRequestPatcher.TryNormalize("""{"model":"gpt-test"}""", true, ["low", "high"], "high");

        ReadEffort(result).Should().Be("high");
        result.Action.Should().Be(ReasoningPatchAction.Defaulted);
    }

    [Fact]
    public void TryNormalize_InvalidDefault_UsesFirstAllowedEffort()
    {
        var result = ReasoningRequestPatcher.TryNormalize("""{"reasoning_effort":"xhigh"}""", true, ["low", "medium"], "xhigh");

        ReadEffort(result).Should().Be("low");
        result.Action.Should().Be(ReasoningPatchAction.Defaulted);
    }

    [Fact]
    public void TryNormalize_ModelWithoutControllableReasoning_StripsAllReasoningFields()
    {
        var result = ReasoningRequestPatcher.TryNormalize(
            """{"reasoning_effort":"high","thinking":true,"output_config":{"include":"reasoning"}}""",
            false, ["high"], "high");

        result.IsSuccess.Should().BeTrue();
        result.Action.Should().Be(ReasoningPatchAction.Stripped);
        using var document = JsonDocument.Parse(result.PatchedJson!);
        document.RootElement.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void TryNormalize_InvalidJson_PreservesHistoricPassThrough()
    {
        const string json = """{"reasoning_effort":"high""";

        var result = ReasoningRequestPatcher.TryNormalize(json, true, ["high"], "high");

        result.IsSuccess.Should().BeTrue();
        result.PatchedJson.Should().Be(json);
        result.Action.Should().Be(ReasoningPatchAction.Unchanged);
    }

    private static string? ReadEffort(ReasoningPatchResult result)
    {
        result.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(result.PatchedJson!);
        document.RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
        return document.RootElement.GetProperty("reasoning_effort").GetString();
    }
}
