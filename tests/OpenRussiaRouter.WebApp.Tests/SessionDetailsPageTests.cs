using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.SiteStats;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Tests for the admin visit-session details page: offset formatting, truncation detection,
/// the not-found contract for an unknown id and the disabled-sink banner. The database-backed
/// <see cref="VisitSessionReader"/> is exercised only in its disabled mode — this unit suite
/// stands up no Postgres, so the id → result decision is driven through
/// <c>ApplyResultAsync</c> instead.
/// </summary>
public sealed class SessionDetailsPageTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "+00:00")]
    [InlineData(9, "+00:09")]
    [InlineData(59, "+00:59")]
    [InlineData(60, "+01:00")]
    [InlineData(65, "+01:05")]
    [InlineData(605, "+10:05")]
    [InlineData(3599, "+59:59")]
    public void FormatOffset_UnderAnHour_RendersMinutesAndSeconds(int seconds, string expected)
    {
        Pages.Admin.SessionDetailsModel
            .FormatOffset(Start, Start.AddSeconds(seconds))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(3600, "+1:00:00")]
    [InlineData(3725, "+1:02:05")]
    [InlineData(7325, "+2:02:05")]
    public void FormatOffset_AnHourOrMore_AddsHours(int seconds, string expected)
    {
        Pages.Admin.SessionDetailsModel
            .FormatOffset(Start, Start.AddSeconds(seconds))
            .Should().Be(expected);
    }

    /// <summary>
    /// A late queue flush can merge a page view that predates the session's stored start.
    /// Clamping keeps the column readable instead of printing a negative offset.
    /// </summary>
    [Fact]
    public void FormatOffset_BeforeSessionStart_ClampsToZero()
    {
        Pages.Admin.SessionDetailsModel
            .FormatOffset(Start, Start.AddSeconds(-30))
            .Should().Be("+00:00");
    }

    /// <summary>Offsets are computed on the instant, not on the printed local time.</summary>
    [Fact]
    public void FormatOffset_DifferentOffsets_ComparesOnUtcInstant()
    {
        var pageAt = Start.AddSeconds(90).ToOffset(TimeSpan.FromHours(3));

        Pages.Admin.SessionDetailsModel.FormatOffset(Start, pageAt).Should().Be("+01:30");
    }

    [Fact]
    public async Task ApplyResultAsync_UnknownId_ReturnsNotFound()
    {
        var model = NewModel();

        var result = await model.ApplyResultAsync(null);

        result.Should().BeOfType<NotFoundResult>();
        model.Details.Should().BeNull();
    }

    [Fact]
    public async Task ApplyResultAsync_ExistingSession_RendersPage()
    {
        var model = NewModel();

        var result = await model.ApplyResultAsync(NewDetails(pageViewCount: 3, storedPages: 3));

        result.Should().BeOfType<PageResult>();
        model.Details.Should().NotBeNull();
        model.Details!.Session.VisitorHash.Should().Be("visitor-1");
    }

    [Fact]
    public async Task TruncationNote_AllPagesStored_IsNull()
    {
        var model = NewModel();
        await model.ApplyResultAsync(NewDetails(pageViewCount: 3, storedPages: 3));

        model.Details!.IsTruncated.Should().BeFalse();
        model.TruncationNote.Should().BeNull();
    }

    /// <summary>
    /// Truncation has no flag column: <c>page_view_count</c> counts every view while the stored
    /// <c>pages</c> array is capped, so the mismatch is the only marker.
    /// </summary>
    [Fact]
    public async Task TruncationNote_PageListCut_StatesShownAndTotal()
    {
        var model = NewModel();
        await model.ApplyResultAsync(NewDetails(pageViewCount: 250, storedPages: 200));

        model.Details!.IsTruncated.Should().BeTrue();
        model.TruncationNote.Should().NotBeNull();
        model.TruncationNote.Should().Contain("200").And.Contain("250");
    }

    [Fact]
    public void TruncationNote_NothingLoaded_IsNull()
    {
        NewModel().TruncationNote.Should().BeNull();
    }

    /// <summary>
    /// A disabled sink is not a missing session: the page must answer <c>200</c> with the same
    /// banner the listing shows, never <c>404</c>.
    /// </summary>
    [Fact]
    public async Task OnGetAsync_SinkDisabled_ShowsBannerInsteadOfNotFound()
    {
        var model = NewModel();

        var result = await model.OnGetAsync(42);

        result.Should().BeOfType<PageResult>();
        model.AnalyticsEnabled.Should().BeFalse();
        model.Details.Should().BeNull();
        model.ErrorMessage.Should().BeNull();
    }

    private static VisitSessionDetails NewDetails(int pageViewCount, int storedPages)
    {
        var session = new VisitSessionEntry(
            Id: 7,
            VisitorHash: "visitor-1",
            DistinctId: null,
            StartedAtUtc: Start,
            EndedAtUtc: Start.AddMinutes(5),
            DurationSeconds: 300,
            PageViewCount: pageViewCount,
            EntryPath: "/",
            ExitPath: "/models",
            ReferrerHost: null,
            ReferrerKind: "direct",
            UtmSource: null,
            UtmMedium: null,
            UtmCampaign: null,
            DeviceClass: "desktop",
            IsAuthenticated: false,
            Environment: "test");

        var pages = Enumerable.Range(0, storedPages)
            .Select(i => new VisitSessionPage(Start.AddSeconds(i), "/p" + i))
            .ToList();

        return new VisitSessionDetails(session, pages);
    }

    /// <summary>
    /// Constructs the model with a disabled <see cref="VisitSessionReader"/> and stub
    /// dependencies. Anonymous sessions never reach auth, so the stub provider is not called
    /// by any of the paths exercised here.
    /// </summary>
    private static Pages.Admin.SessionDetailsModel NewModel()
    {
        var reader = new VisitSessionReader(
            dataSource: null,
            pseudonymizer: null,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<VisitSessionReader>.Instance);
        var authProvider = new OpenRussiaRouter.WebApp.Services.Auth.AuthProvider(client: null!);
        var pseudonymizer = new UserPseudonymizer(
            new ProductAnalyticsOptions { IdentitySalt = "test-salt" });

        return new Pages.Admin.SessionDetailsModel(
            reader,
            authProvider,
            pseudonymizer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Pages.Admin.SessionDetailsModel>.Instance);
    }
}
