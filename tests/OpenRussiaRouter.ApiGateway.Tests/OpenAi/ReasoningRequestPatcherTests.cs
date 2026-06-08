using System.Text.Json;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.OpenAi;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.ApiGateway.Tests.OpenAi;

public sealed class ReasoningRequestPatcherTests
{
    [Fact]
    public void TryNormalize_NoReasoningFields_ReturnsBodyAsIs()
    {
        const string json = """{"model":"gpt-test"}""";

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["low", "medium"]);

        result.IsSuccess.Should().BeTrue();
        result.PatchedJson.Should().Be(json);
    }

    [Fact]
    public void TryNormalize_InvalidJson_ReturnsBodyAsIs()
    {
        const string json = """{"reasoning_effort":"high""";

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["high"]);

        result.IsSuccess.Should().BeTrue();
        result.PatchedJson.Should().Be(json);
    }

    [Fact]
    public void TryNormalize_FlatReasoningEffort_NormalizesAndKeepsFlatField()
    {
        const string json = """
        {
          "model": "gpt-test",
          "reasoning_effort": " High "
        }
        """;

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["low", "high"]);

        result.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(result.PatchedJson);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("high");
    }

    [Fact]
    public void TryNormalize_NestedReasoningEffort_FlattensAndRemovesObject()
    {
        const string json = """
        {
          "model": "gpt-test",
          "reasoning": {
            "effort": "Medium"
          }
        }
        """;

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["medium", "high"]);

        result.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(result.PatchedJson);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("medium");
        document.RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_BothEffortsMatchDifferentCase_AcceptsRequest()
    {
        const string json = """
        {
          "reasoning_effort": "HIGH",
          "reasoning": {
            "effort": " high "
          }
        }
        """;

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["high"]);

        result.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(result.PatchedJson);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("high");
        document.RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_BothEffortsConflict_ReturnsConflictingParameters()
    {
        const string json = """
        {
          "reasoning_effort": "low",
          "reasoning": {
            "effort": "high"
          }
        }
        """;

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["low", "high"]);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ConflictingParameters);
    }

    [Theory]
    [InlineData("max_tokens", "{\"reasoning\":{\"max_tokens\":1000,\"effort\":\"high\"}}")]
    [InlineData("enabled", "{\"reasoning\":{\"enabled\":true,\"effort\":\"high\"}}")]
    public void TryNormalize_UnsupportedReasoningShape_ReturnsUnsupportedParameter(string _, string json)
    {
        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["high"]);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnsupportedReasoning);
        result.ErrorMessage.Should().Be("reasoning.max_tokens/enabled not supported yet, use reasoning.effort");
    }

    [Fact]
    public void TryNormalize_ModelDoesNotSupportReasoning_ReturnsUnsupportedParameter()
    {
        const string json = """{"reasoning_effort":"high"}""";

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: false, ["high"]);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnsupportedReasoning);
        result.ErrorMessage.Should().Be("Model does not support reasoning");
    }

    [Fact]
    public void TryNormalize_EffortOutsideCapability_ReturnsUnsupportedReasoningEffort()
    {
        const string json = """{"reasoning_effort":"xhigh"}""";

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["low", "medium", "high"]);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnsupportedReasoningEffort);
        result.ErrorMessage.Should().Be("Model supports reasoning efforts: low, medium, high");
    }

    [Fact]
    public void TryNormalize_NoneEffort_IsAcceptedAsExplicitOff()
    {
        const string json = """{"reasoning":{"effort":"none"}}""";

        var result = ReasoningRequestPatcher.TryNormalize(json, supportsReasoning: true, ["none", "low"]);

        result.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(result.PatchedJson);
        document.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("none");
    }
}
