using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class VisitSessionAggregatorTests
{
    private const int MaxPages = 200;
    private static readonly DateTimeOffset Base = new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    private static CompactableVisit Visit(
        long id,
        DateTimeOffset occurredAt,
        string path = "/",
        string? distinctId = null,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null,
        bool isAuthenticated = false,
        string? referrerHost = null,
        string referrerKind = SiteVisitReferrerKinds.Direct,
        string deviceClass = SiteVisitDeviceClasses.Desktop,
        string environment = "test") =>
        new(
            Id: id,
            VisitorHash: "visitor-1",
            DistinctId: distinctId,
            OccurredAt: occurredAt,
            Path: path,
            ReferrerHost: referrerHost,
            ReferrerKind: referrerKind,
            UtmSource: utmSource,
            UtmMedium: utmMedium,
            UtmCampaign: utmCampaign,
            DeviceClass: deviceClass,
            IsAuthenticated: isAuthenticated,
            Environment: environment);

    [Fact]
    public void Aggregate_UsesFirstAndLastVisitForEntryAndExit()
    {
        var slice = new[]
        {
            Visit(1, Base, "/"),
            Visit(2, Base.AddMinutes(2), "/models"),
            Visit(3, Base.AddMinutes(9), "/signup"),
        };

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.EntryPath.Should().Be("/");
        row.ExitPath.Should().Be("/signup");
        row.StartedAt.Should().Be(Base);
        row.EndedAt.Should().Be(Base.AddMinutes(9));
        row.DurationSeconds.Should().Be(540);
        row.PageViewCount.Should().Be(3);
        row.VisitorHash.Should().Be("visitor-1");
    }

    [Fact]
    public void Aggregate_UnsortedSlice_StillUsesTimeOrder()
    {
        var slice = new[]
        {
            Visit(3, Base.AddMinutes(9), "/signup"),
            Visit(1, Base, "/"),
            Visit(2, Base.AddMinutes(2), "/models"),
        };

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.EntryPath.Should().Be("/");
        row.ExitPath.Should().Be("/signup");
        row.Pages.Select(p => p.Path).Should().Equal("/", "/models", "/signup");
    }

    [Fact]
    public void Aggregate_SingleVisit_HasZeroDuration()
    {
        var row = VisitSessionAggregator.Aggregate(new[] { Visit(1, Base, "/docs") }, MaxPages);

        row.DurationSeconds.Should().Be(0);
        row.PageViewCount.Should().Be(1);
        row.EntryPath.Should().Be("/docs");
        row.ExitPath.Should().Be("/docs");
    }

    [Fact]
    public void Aggregate_UtmTags_TakeFirstNonEmpty()
    {
        var slice = new[]
        {
            Visit(1, Base, utmSource: null, utmMedium: "   ", utmCampaign: null),
            Visit(2, Base.AddMinutes(1), utmSource: "yandex", utmMedium: "cpc", utmCampaign: "launch"),
            Visit(3, Base.AddMinutes(2), utmSource: "google", utmMedium: "organic", utmCampaign: "other"),
        };

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.UtmSource.Should().Be("yandex");
        row.UtmMedium.Should().Be("cpc");
        row.UtmCampaign.Should().Be("launch");
    }

    [Fact]
    public void Aggregate_NoUtmTags_LeavesThemNull()
    {
        var row = VisitSessionAggregator.Aggregate(new[] { Visit(1, Base, utmSource: "  ") }, MaxPages);

        row.UtmSource.Should().BeNull();
        row.UtmMedium.Should().BeNull();
        row.UtmCampaign.Should().BeNull();
    }

    [Fact]
    public void Aggregate_DistinctId_TakesLastNonEmpty()
    {
        // The visitor browsed anonymously, signed in mid-session, then kept browsing.
        var slice = new[]
        {
            Visit(1, Base, "/"),
            Visit(2, Base.AddMinutes(1), "/login", distinctId: "user-a"),
            Visit(3, Base.AddMinutes(2), "/workspace", distinctId: null),
            Visit(4, Base.AddMinutes(3), "/workspace/credits", distinctId: "user-b"),
        };

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.DistinctId.Should().Be("user-b");
    }

    [Fact]
    public void Aggregate_AnonymousSession_HasNullDistinctId()
    {
        var row = VisitSessionAggregator.Aggregate(new[] { Visit(1, Base, distinctId: "  ") }, MaxPages);

        row.DistinctId.Should().BeNull();
    }

    [Fact]
    public void Aggregate_IsAuthenticated_IsOrOverVisits()
    {
        var slice = new[]
        {
            Visit(1, Base, isAuthenticated: false),
            Visit(2, Base.AddMinutes(1), isAuthenticated: true),
            Visit(3, Base.AddMinutes(2), isAuthenticated: false),
        };

        VisitSessionAggregator.Aggregate(slice, MaxPages).IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Aggregate_AllAnonymousVisits_IsNotAuthenticated()
    {
        var slice = new[] { Visit(1, Base), Visit(2, Base.AddMinutes(1)) };

        VisitSessionAggregator.Aggregate(slice, MaxPages).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_ContextFields_ComeFromFirstVisit()
    {
        var slice = new[]
        {
            Visit(
                1,
                Base,
                referrerHost: "ya.ru",
                referrerKind: SiteVisitReferrerKinds.Search,
                deviceClass: SiteVisitDeviceClasses.Mobile,
                environment: "production"),
            Visit(
                2,
                Base.AddMinutes(1),
                referrerHost: "openrussiarouter.ru",
                referrerKind: SiteVisitReferrerKinds.Internal,
                deviceClass: SiteVisitDeviceClasses.Desktop,
                environment: "staging"),
        };

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.ReferrerHost.Should().Be("ya.ru");
        row.ReferrerKind.Should().Be(SiteVisitReferrerKinds.Search);
        row.DeviceClass.Should().Be(SiteVisitDeviceClasses.Mobile);
        row.Environment.Should().Be("production");
    }

    [Fact]
    public void Aggregate_PagesTruncated_PageViewCountStillCountsAll()
    {
        var slice = Enumerable.Range(0, 250)
            .Select(i => Visit(i + 1, Base.AddSeconds(i), $"/p{i}"))
            .ToArray();

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.PageViewCount.Should().Be(250);
        row.Pages.Should().HaveCount(MaxPages);
        row.Pages[0].Path.Should().Be("/p0");
        row.Pages[^1].Path.Should().Be("/p199");
        row.ExitPath.Should().Be("/p249");
        row.PageViewCount.Should().BeGreaterThan(row.Pages.Count);
    }

    [Fact]
    public void Aggregate_PagesBelowLimit_AreKeptWhole()
    {
        var slice = new[] { Visit(1, Base, "/"), Visit(2, Base.AddSeconds(30), "/models") };

        var row = VisitSessionAggregator.Aggregate(slice, MaxPages);

        row.Pages.Should().HaveCount(2);
        row.Pages[0].Should().Be(new VisitSessionPage(Base, "/"));
        row.Pages[1].Should().Be(new VisitSessionPage(Base.AddSeconds(30), "/models"));
    }

    [Fact]
    public void Aggregate_ZeroMaxPages_KeepsCountersButNoPages()
    {
        var row = VisitSessionAggregator.Aggregate(new[] { Visit(1, Base), Visit(2, Base.AddMinutes(1)) }, 0);

        row.Pages.Should().BeEmpty();
        row.PageViewCount.Should().Be(2);
    }

    [Fact]
    public void Aggregate_NonZeroOffsets_DurationIsComputedInUtc()
    {
        var first = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(3));
        var second = new DateTimeOffset(2026, 7, 27, 9, 10, 0, TimeSpan.Zero);

        var row = VisitSessionAggregator.Aggregate(new[] { Visit(1, first), Visit(2, second) }, MaxPages);

        row.DurationSeconds.Should().Be(600);
        row.StartedAt.Should().Be(first);
        row.EndedAt.Should().Be(second);
    }

    [Fact]
    public void Aggregate_EmptySlice_Throws()
    {
        var act = () => VisitSessionAggregator.Aggregate(Array.Empty<CompactableVisit>(), MaxPages);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Aggregate_NullSlice_Throws()
    {
        var act = () => VisitSessionAggregator.Aggregate(null!, MaxPages);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Aggregate_NegativeMaxPages_Throws()
    {
        var act = () => VisitSessionAggregator.Aggregate(new[] { Visit(1, Base) }, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Aggregate_OfSessionizerSlices_ProducesOneRowPerSession()
    {
        var visits = new[]
        {
            Visit(1, Base, "/"),
            Visit(2, Base.AddMinutes(3), "/models"),
            Visit(3, Base.AddMinutes(90), "/docs"),
        };

        var rows = VisitSessionizer.Split(visits, TimeSpan.FromMinutes(30))
            .Select(slice => VisitSessionAggregator.Aggregate(slice, MaxPages))
            .ToList();

        rows.Should().HaveCount(2);
        rows[0].PageViewCount.Should().Be(2);
        rows[0].ExitPath.Should().Be("/models");
        rows[1].PageViewCount.Should().Be(1);
        rows[1].EntryPath.Should().Be("/docs");
    }
}
