using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class VisitSessionizerTests
{
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(30);
    private static readonly DateTimeOffset Base = new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    private static CompactableVisit Visit(long id, DateTimeOffset occurredAt, string path = "/") =>
        new(
            Id: id,
            VisitorHash: "visitor-1",
            DistinctId: null,
            OccurredAt: occurredAt,
            Path: path,
            ReferrerHost: null,
            ReferrerKind: SiteVisitReferrerKinds.Direct,
            UtmSource: null,
            UtmMedium: null,
            UtmCampaign: null,
            DeviceClass: SiteVisitDeviceClasses.Desktop,
            IsAuthenticated: false,
            Environment: "test");

    [Fact]
    public void Split_EmptyInput_ReturnsEmptyList()
    {
        VisitSessionizer.Split(Array.Empty<CompactableVisit>(), Gap).Should().BeEmpty();
    }

    [Fact]
    public void Split_SingleVisit_ReturnsOneSliceOfOne()
    {
        var slices = VisitSessionizer.Split(new[] { Visit(1, Base) }, Gap);

        slices.Should().ContainSingle();
        slices[0].Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void Split_PauseJustUnderGap_KeepsOneSession()
    {
        var slices = VisitSessionizer.Split(
            new[] { Visit(1, Base), Visit(2, Base.AddMinutes(29).AddSeconds(59)) },
            Gap);

        slices.Should().ContainSingle();
        slices[0].Should().HaveCount(2);
    }

    [Fact]
    public void Split_PauseExactlyGap_KeepsOneSession()
    {
        // The boundary is strict: `delta > gap`. Exactly 30:00 is still the same sitting.
        var slices = VisitSessionizer.Split(
            new[] { Visit(1, Base), Visit(2, Base.AddMinutes(30)) },
            Gap);

        slices.Should().ContainSingle();
        slices[0].Should().HaveCount(2);
    }

    [Fact]
    public void Split_PauseJustOverGap_StartsNewSession()
    {
        var slices = VisitSessionizer.Split(
            new[] { Visit(1, Base), Visit(2, Base.AddMinutes(30).AddMilliseconds(1)) },
            Gap);

        slices.Should().HaveCount(2);
        slices[0].Should().ContainSingle();
        slices[1].Should().ContainSingle();
    }

    [Fact]
    public void Split_PauseOneSecondOverGap_StartsNewSession()
    {
        var slices = VisitSessionizer.Split(
            new[] { Visit(1, Base), Visit(2, Base.AddMinutes(30).AddSeconds(1)) },
            Gap);

        slices.Should().HaveCount(2);
    }

    [Fact]
    public void Split_EqualTimestamps_OrdersById()
    {
        var slices = VisitSessionizer.Split(
            new[] { Visit(7, Base, "/b"), Visit(3, Base, "/a") },
            Gap);

        slices.Should().ContainSingle();
        slices[0].Select(v => v.Id).Should().Equal(3, 7);
    }

    [Fact]
    public void Split_UnsortedInput_MatchesSortedInput()
    {
        var sorted = new[]
        {
            Visit(1, Base),
            Visit(2, Base.AddMinutes(10)),
            Visit(3, Base.AddMinutes(50)),
            Visit(4, Base.AddMinutes(55)),
        };
        var shuffled = new[] { sorted[2], sorted[0], sorted[3], sorted[1] };

        var fromSorted = VisitSessionizer.Split(sorted, Gap);
        var fromShuffled = VisitSessionizer.Split(shuffled, Gap);

        fromShuffled.Select(s => s.Select(v => v.Id).ToArray())
            .Should().BeEquivalentTo(
                fromSorted.Select(s => s.Select(v => v.Id).ToArray()),
                o => o.WithStrictOrdering());
        fromSorted.Should().HaveCount(2);
    }

    [Fact]
    public void Split_UtcMidnightCrossing_StartsNewSessionEvenWithTinyGap()
    {
        var beforeMidnight = new DateTimeOffset(2026, 7, 27, 23, 59, 30, TimeSpan.Zero);

        var slices = VisitSessionizer.Split(
            new[] { Visit(1, beforeMidnight), Visit(2, beforeMidnight.AddMinutes(1)) },
            Gap);

        slices.Should().HaveCount(2);
    }

    [Fact]
    public void Split_UtcMidnightCrossing_DayBoundaryDisabled_KeepsOneSession()
    {
        var beforeMidnight = new DateTimeOffset(2026, 7, 27, 23, 59, 30, TimeSpan.Zero);

        var slices = VisitSessionizer.Split(
            new[] { Visit(1, beforeMidnight), Visit(2, beforeMidnight.AddMinutes(1)) },
            Gap,
            splitOnUtcDayBoundary: false);

        slices.Should().ContainSingle();
        slices[0].Should().HaveCount(2);
    }

    [Fact]
    public void Split_NonZeroOffset_LocalDayChangeWithinSameUtcDay_KeepsOneSession()
    {
        // Local dates differ (26th -> 27th), UTC date is the same 2026-07-27.
        var first = new DateTimeOffset(2026, 7, 26, 23, 50, 0, TimeSpan.FromHours(-5));
        var second = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.FromHours(-5));

        var slices = VisitSessionizer.Split(new[] { Visit(1, first), Visit(2, second) }, Gap);

        slices.Should().ContainSingle();
        slices[0].Should().HaveCount(2);
    }

    [Fact]
    public void Split_NonZeroOffset_UtcDayChangeWithinSameLocalDay_StartsNewSession()
    {
        // Local date is the same 2026-07-27, UTC date rolls over to the 28th.
        var first = new DateTimeOffset(2026, 7, 27, 18, 50, 0, TimeSpan.FromHours(-5));
        var second = new DateTimeOffset(2026, 7, 27, 19, 5, 0, TimeSpan.FromHours(-5));

        var slices = VisitSessionizer.Split(new[] { Visit(1, first), Visit(2, second) }, Gap);

        slices.Should().HaveCount(2);
    }

    [Fact]
    public void Split_LongIdleCrawler_ProducesOneSessionPerUtcDay()
    {
        // A crawler polling every five minutes must not accumulate an endless session.
        var visits = new List<CompactableVisit>();
        var cursor = new DateTimeOffset(2026, 7, 27, 22, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 24 * 12; i++)
        {
            visits.Add(Visit(i + 1, cursor));
            cursor = cursor.AddMinutes(5);
        }

        // 288 visits, ~24h of traffic starting at 22:00 UTC: one midnight is crossed,
        // so the never-idle crawler still gets its session closed once per UTC day.
        var slices = VisitSessionizer.Split(visits, Gap);

        slices.Should().HaveCount(2);
        slices.SelectMany(s => s).Should().HaveCount(visits.Count);
        slices[0].Should().OnlyContain(v => v.OccurredAt.UtcDateTime.Day == 27);
        slices[1].Should().OnlyContain(v => v.OccurredAt.UtcDateTime.Day == 28);
    }

    [Fact]
    public void Split_MultipleGaps_SplitsEveryTime()
    {
        var visits = new[]
        {
            Visit(1, Base),
            Visit(2, Base.AddMinutes(5)),
            Visit(3, Base.AddMinutes(60)),
            Visit(4, Base.AddMinutes(200)),
            Visit(5, Base.AddMinutes(210)),
        };

        var slices = VisitSessionizer.Split(visits, Gap);

        slices.Select(s => s.Count).Should().Equal(2, 1, 2);
    }

    [Fact]
    public void Split_NullVisits_Throws()
    {
        var act = () => VisitSessionizer.Split(null!, Gap);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Split_NegativeGap_Throws()
    {
        var act = () => VisitSessionizer.Split(new[] { Visit(1, Base) }, TimeSpan.FromMinutes(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
