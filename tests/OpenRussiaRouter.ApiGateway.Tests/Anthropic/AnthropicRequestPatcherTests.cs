using System.Text.Json.Nodes;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.Anthropic;

namespace OpenRussiaRouter.ApiGateway.Tests.Anthropic;

public sealed class AnthropicRequestPatcherTests
{
    [Fact]
    public void PatchModel_ReplacesModelField()
    {
        var node = JsonNode.Parse("""{"model":"ORR/Claude-3-7-Sonnet","messages":[{"role":"user","content":"hi"}]}""")!;
        AnthropicRequestPatcher.PatchModel(node, "claude-3-7-sonnet-20250219");

        node["model"]!.GetValue<string>().Should().Be("claude-3-7-sonnet-20250219");
    }

    [Fact]
    public void NormalizeReasoning_SupportedModel_InjectsOutputConfigEffort()
    {
        var node = JsonNode.Parse("""{"model":"test","output_config":{"effort":"high"}}""")!;
        var metadata = AnthropicRequestReader.ReadMetadata(node.ToJsonString());

        var result = AnthropicRequestPatcher.TryNormalizeReasoningInPlace(
            node,
            metadata.Reasoning,
            supportsReasoning: true,
            allowedEfforts: ["low", "medium", "high"],
            defaultEffort: "medium");

        result.Action.Should().Be(AnthropicReasoningPatchAction.Applied);
        node["output_config"]!["effort"]!.GetValue<string>().Should().Be("high");
    }

    [Fact]
    public void NormalizeReasoning_UnsupportedModel_StripsReasoningFields()
    {
        var node = JsonNode.Parse("""{"model":"test","output_config":{"effort":"high"},"thinking":{"type":"enabled","budget_tokens":2000}}""")!;
        var metadata = AnthropicRequestReader.ReadMetadata(node.ToJsonString());

        var result = AnthropicRequestPatcher.TryNormalizeReasoningInPlace(
            node,
            metadata.Reasoning,
            supportsReasoning: false,
            allowedEfforts: []);

        result.Action.Should().Be(AnthropicReasoningPatchAction.Stripped);
        node["output_config"].Should().BeNull();
        node["thinking"].Should().BeNull();
    }

    [Fact]
    public void NormalizeReasoning_InvalidEffort_FallsBackToDefault()
    {
        var node = JsonNode.Parse("""{"model":"test","output_config":{"effort":"extreme"}}""")!;
        var metadata = AnthropicRequestReader.ReadMetadata(node.ToJsonString());

        var result = AnthropicRequestPatcher.TryNormalizeReasoningInPlace(
            node,
            metadata.Reasoning,
            supportsReasoning: true,
            allowedEfforts: ["low", "medium", "high"],
            defaultEffort: "medium");

        result.Action.Should().Be(AnthropicReasoningPatchAction.Defaulted);
        node["output_config"]!["effort"]!.GetValue<string>().Should().Be("medium");
    }
}
