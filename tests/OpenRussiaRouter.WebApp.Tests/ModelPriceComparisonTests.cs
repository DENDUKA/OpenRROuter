using FluentAssertions;
using OpenRussiaRouter.WebApp.Pages.Admin;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Delta arithmetic behind the admin price-comparison page: sign convention (negative = we are
/// cheaper) and the non-comparable cases that must stay null instead of rendering a bogus percentage.
/// </summary>
public class ModelPriceComparisonTests
{
    private static ModelPricesModel.PriceComparisonRow Row(
        decimal ourInput,
        decimal ourOutput,
        decimal? orInput,
        decimal? orOutput,
        string? matchedId = "openai/gpt-4o") =>
        new(
            "ORR/Model",
            "Model",
            "model",
            "OpenAI",
            true,
            ourInput,
            ourOutput,
            0m,
            matchedId,
            orInput,
            orOutput,
            null);

    [Fact]
    public void CheaperModel_ReportsNegativeDelta()
    {
        var row = Row(ourInput: 0.5m, ourOutput: 1.5m, orInput: 1m, orOutput: 3m);

        row.InputDiffPercent.Should().Be(-50m);
        row.OutputDiffPercent.Should().Be(-50m);
        row.TotalDiffPercent.Should().Be(-50m);
    }

    [Fact]
    public void PricierModel_ReportsPositiveDelta()
    {
        var row = Row(ourInput: 1.25m, ourOutput: 4m, orInput: 1m, orOutput: 2m);

        row.InputDiffPercent.Should().Be(25m);
        row.OutputDiffPercent.Should().Be(100m);
        row.TotalDiffPercent.Should().Be(75m);
    }

    [Fact]
    public void SamePrice_ReportsZeroDelta()
    {
        var row = Row(ourInput: 1m, ourOutput: 2m, orInput: 1m, orOutput: 2m);

        row.TotalDiffPercent.Should().Be(0m);
    }

    [Fact]
    public void UnmatchedModel_HasNoDelta()
    {
        var row = Row(ourInput: 1m, ourOutput: 2m, orInput: null, orOutput: null, matchedId: null);

        row.Matched.Should().BeFalse();
        row.InputDiffPercent.Should().BeNull();
        row.TotalDiffPercent.Should().BeNull();
    }

    [Fact]
    public void FreeUpstream_IsNotComparableUnlessWeAreFreeToo()
    {
        Row(ourInput: 1m, ourOutput: 2m, orInput: 0m, orOutput: 0m).InputDiffPercent.Should().BeNull();
        Row(ourInput: 0m, ourOutput: 0m, orInput: 0m, orOutput: 0m).InputDiffPercent.Should().Be(0m);
    }

    [Fact]
    public void PartialOpenRouterPricing_StillProducesTotalDelta()
    {
        // OpenRouter sometimes exposes only one side of the pricing; the missing half counts as 0.
        var row = Row(ourInput: 1m, ourOutput: 1m, orInput: 2m, orOutput: null);

        row.OutputDiffPercent.Should().BeNull();
        row.TotalDiffPercent.Should().Be(0m);
    }
}
