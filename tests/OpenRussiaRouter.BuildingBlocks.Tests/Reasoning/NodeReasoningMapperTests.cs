using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Reasoning;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Reasoning;

public sealed class NodeReasoningMapperTests
{
    [Fact]
    public void MapFromNodeOptions_EmptyInput_ReturnsEmpty()
    {
        NodeReasoningMapper.MapFromNodeOptions([]).Should().BeEmpty();
    }

    [Fact]
    public void MapFromNodeOptions_NullInput_ReturnsEmpty()
    {
        NodeReasoningMapper.MapFromNodeOptions(null).Should().BeEmpty();
    }

    [Fact]
    public void MapFromNodeOptions_EffortVocabulary_PassesThroughInCanonicalOrder()
    {
        // 9Router "effort" family emits ["auto","none","low","medium","high"] — "auto" drops.
        var input = new[] { "auto", "high", "low", "medium", "none" };

        var result = NodeReasoningMapper.MapFromNodeOptions(input);

        result.Should().Equal(["none", "low", "medium", "high"]);
    }

    [Fact]
    public void MapFromNodeOptions_ExtendedVocabulary_TranslatesOnOffAndDropsAuto()
    {
        // 9Router "extended" family (claude-adaptive, gemini-level) emits ["auto","on","off"].
        var input = new[] { "auto", "on", "off" };

        var result = NodeReasoningMapper.MapFromNodeOptions(input);

        // "auto" → dropped, "on" → high, "off" → none, ordered low → high.
        result.Should().Equal(["none", "high"]);
    }

    [Fact]
    public void MapFromNodeOptions_MixedVocabulary_UnionsAndOrders()
    {
        // Drift case from CollectorProjection union across nodes with different thinkingFormat.
        var input = new[] { "auto", "on", "off", "low", "medium", "high" };

        var result = NodeReasoningMapper.MapFromNodeOptions(input);

        result.Should().Equal(["none", "low", "medium", "high"]);
    }

    [Fact]
    public void MapFromNodeOptions_DeduplicatesAndNormalizesCase()
    {
        var input = new[] { "Medium", "medium", "LOW", "low" };

        var result = NodeReasoningMapper.MapFromNodeOptions(input);

        result.Should().Equal(["low", "medium"]);
    }

    [Fact]
    public void MapFromNodeOptions_AutoOnly_ReturnsEmpty()
    {
        // A node that only advertises "auto" carries no actionable canonical effort.
        NodeReasoningMapper.MapFromNodeOptions(["auto"]).Should().BeEmpty();
    }

    [Fact]
    public void MapFromNodeOptions_StripsUnknownTokens()
    {
        var input = new[] { "turbo", "minimal", "xhigh", "max", "ultra", "thinking", "" };

        var result = NodeReasoningMapper.MapFromNodeOptions(input);

        result.Should().Equal(["minimal", "xhigh", "max", "ultra", "thinking"]);
    }

    [Fact]
    public void PickDefaultEffort_NullOrEmpty_ReturnsNull()
    {
        NodeReasoningMapper.PickDefaultEffort(null).Should().BeNull();
        NodeReasoningMapper.PickDefaultEffort([]).Should().BeNull();
    }

    [Fact]
    public void PickDefaultEffort_PrefersMedium()
    {
        NodeReasoningMapper.PickDefaultEffort(["low", "medium", "high"]).Should().Be("medium");
    }

    [Fact]
    public void PickDefaultEffort_FallsBackToHighThenLow()
    {
        NodeReasoningMapper.PickDefaultEffort(["high"]).Should().Be("high");
        NodeReasoningMapper.PickDefaultEffort(["low"]).Should().Be("low");
        NodeReasoningMapper.PickDefaultEffort(["low", "high"]).Should().Be("high");
    }

    [Fact]
    public void PickDefaultEffort_FallsBackToFirstAvailableWhenNoStandard()
    {
        // Niche case: model only allows minimal/xhigh — pick the first declared.
        NodeReasoningMapper.PickDefaultEffort(["minimal", "xhigh"]).Should().Be("minimal");
    }
}
