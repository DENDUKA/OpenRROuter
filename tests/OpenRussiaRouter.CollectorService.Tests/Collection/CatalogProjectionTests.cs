using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class CatalogProjectionTests
{
    private static ModelObservation Obs(string upstream, string publicId, int rank = 2, string type = "", params string[] sources) =>
        new() { UpstreamId = upstream, PublicId = publicId, ModelType = type, Rank = rank, SourceProviders = sources };

    [Fact]
    public void Capabilities_UsesPublicIdWhenPresent_ElseUpstream_OrderedAndDistinct()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false,
                [Obs("up-b", "pub-b"), Obs("up-a", ""), Obs("up-b", "pub-b")], null, null)],
            DateTimeOffset.UnixEpoch);

        var caps = CatalogProjection.Capabilities(snapshot);

        caps.Should().ContainSingle();
        caps[0].NodeId.Should().Be("n1");
        caps[0].ModelIds.Should().Equal("pub-b", "up-a");
    }

    [Fact]
    public void Capabilities_SkipsUnreachableNodes()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("dead", true, [], "req", "resp")],
            DateTimeOffset.UnixEpoch);

        CatalogProjection.Capabilities(snapshot).Should().BeEmpty();
    }

    [Fact]
    public void Capabilities_ExposesComboAlias()
    {
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false, [Obs("deepseek-v4-flash", "deepseek-v4-flash", rank: 3)], null, null)],
            DateTimeOffset.UnixEpoch);

        var caps = CatalogProjection.Capabilities(snapshot);

        caps.Should().ContainSingle();
        caps[0].ModelIds.Should().ContainSingle().Which.Should().Be("deepseek-v4-flash");
    }

    [Fact]
    public void Catalog_RichRank2_OverridesFallbackRank1_AndMergesNodeIds()
    {
        var snapshot = new CollectorSnapshot(
        [
            new NodeCatalogResult("n1", false, [Obs("gpt", "gpt", rank: 1)], null, null),
            new NodeCatalogResult("n2", false, [Obs("gpt", "gpt-public", rank: 2, type: "text", "openai")], null, null),
        ], DateTimeOffset.UnixEpoch);

        var catalog = CatalogProjection.Catalog(snapshot);

        catalog.Should().ContainSingle();
        var entry = catalog[0];
        entry.UpstreamId.Should().Be("gpt");
        entry.PublicId.Should().Be("gpt-public");
        entry.ModelType.Should().Be("text");
        entry.SourceProviders.Should().Equal("openai");
        entry.NodeIds.Should().Equal("n1", "n2");
    }

    [Fact]
    public void Catalog_ComboWithEmptyIntersection_RemainsNotReasoningCapable()
    {
        var combo = new ModelObservation
        {
            UpstreamId = "public-combo",
            PublicId = "public-combo",
            Rank = 3,
            SupportsReasoning = false,
            ReasoningEfforts = [],
            ComboReasoningMembers =
            [
                new ComboReasoningMember("first/model", ["low", "medium"]),
                new ComboReasoningMember("second/model", [])
            ]
        };
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false, [combo], null, null)], DateTimeOffset.UnixEpoch);

        var entry = CatalogProjection.Catalog(snapshot).Should().ContainSingle().Subject;

        entry.SupportsReasoning.Should().BeFalse();
        entry.ReasoningEfforts.Should().BeEmpty();
        entry.ComboReasoningMembers.Should().ContainSingle(member =>
            member.ModelId == "second/model" && member.ReasoningEfforts.Count == 0);
    }

    [Fact]
    public void NodeModels_ReturnsNullForUnknownNode()
    {
        var snapshot = new CollectorSnapshot([], DateTimeOffset.UnixEpoch);
        CatalogProjection.NodeModels(snapshot, "missing").Should().BeNull();
    }
}
