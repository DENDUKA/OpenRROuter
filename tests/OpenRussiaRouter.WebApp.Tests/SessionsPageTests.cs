using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Pure-function tests for the admin visit-session list page. Same scope as
/// <see cref="VisitorsPageTests"/>: query-string plumbing, date parsing, pager arithmetic and
/// duration formatting. The database-backed
/// <see cref="Services.SiteStats.VisitSessionReader"/> needs a Postgres sink that this unit
/// suite does not stand up.
/// </summary>
public sealed class SessionsPageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void ParseDate_InvalidOrNull_ReturnsNull(string? value)
    {
        Pages.Admin.SessionsModel.ParseDate(value).Should().BeNull();
    }

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024-12-31")]
    public void ParseDate_Valid_ReturnsOffset(string value)
    {
        var parsed = Pages.Admin.SessionsModel.ParseDate(value);
        parsed.Should().NotBeNull();
        parsed!.Value.Year.Should().Be(2024);
    }

    [Theory]
    [InlineData("2024-01-15T10:30:00Z")]
    [InlineData("2024-01-15T10:30:00+03:00")]
    public void ParseDate_FullIso_ReturnsNull(string value)
    {
        Pages.Admin.SessionsModel.ParseDate(value).Should().BeNull();
    }

    [Fact]
    public void HasActiveFilter_NoneSet_ReturnsFalse()
    {
        var model = NewModel();
        model.HasActiveFilter().Should().BeFalse();
    }

    [Theory]
    [InlineData("u1", null, null, null, true)]
    [InlineData(null, "abc", null, null, true)]
    [InlineData(null, null, "2024-01-15", null, true)]
    [InlineData(null, null, null, "2024-01-15", true)]
    [InlineData("   ", "  ", null, null, false)]
    public void HasActiveFilter_VariousStates(string? userId, string? visitorHash,
        string? from, string? to, bool expected)
    {
        var model = NewModel();
        model.UserId = userId;
        model.VisitorHash = visitorHash;
        model.From = from;
        model.To = to;

        model.HasActiveFilter().Should().Be(expected);
    }

    /// <summary>
    /// "Show bots" is a real filter — the default listing hides bots, so ticking the box has
    /// to light up the Clear button just like a text filter does.
    /// </summary>
    [Fact]
    public void HasActiveFilter_IncludeBotsOnly_ReturnsTrue()
    {
        var model = NewModel();
        model.IncludeBots = true;

        model.HasActiveFilter().Should().BeTrue();
    }

    [Fact]
    public void BuildPageUrl_NoFiltersAndFirstPage_ReturnsBarePath()
    {
        var model = NewModel();
        model.BuildPageUrl(0).Should().Be("/admin/sessions");
    }

    [Fact]
    public void BuildPageUrl_PageIndex_IncludesP()
    {
        var model = NewModel();
        model.BuildPageUrl(3).Should().Be("/admin/sessions?p=3");
    }

    [Fact]
    public void BuildPageUrl_AllFilters_EscapedAndOrderedPageFirst()
    {
        var model = NewModel();
        model.UserId = "abc def";
        model.VisitorHash = "h/g";
        model.From = "2024-01-01";
        model.To = "2024-12-31";
        model.IncludeBots = true;

        var url = model.BuildPageUrl(2);

        url.Should().StartWith("/admin/sessions?p=2&");
        url.Should().Contain("userId=abc%20def");
        url.Should().Contain("visitorHash=h%2Fg");
        url.Should().Contain("from=2024-01-01");
        url.Should().Contain("to=2024-12-31");
        url.Should().Contain("bots=true");
    }

    /// <summary>Paging must not silently drop the bot switch, or page 2 would show a
    /// different population than page 1.</summary>
    [Fact]
    public void BuildPageUrl_BotsOff_OmitsFlag()
    {
        var model = NewModel();
        model.IncludeBots = false;

        model.BuildPageUrl(1).Should().Be("/admin/sessions?p=1");
    }

    [Fact]
    public void Pagination_EmptyCount_HasNoPrevOrNext()
    {
        var model = NewModel();
        model.TotalCount = 0;

        model.HasPreviousPage.Should().BeFalse();
        model.TotalPages.Should().Be(0);
        model.HasNextPage.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 200, 4, false, true)]
    [InlineData(3, 200, 4, true, false)]
    [InlineData(0, 51, 2, false, true)]
    public void Pagination_State_DerivesFromTotalCount(int pageNumber, int totalCount,
        int expectedTotalPages, bool hasPrev, bool hasNext)
    {
        var model = NewModel();
        model.PageNumber = pageNumber;
        model.TotalCount = totalCount;

        model.TotalPages.Should().Be(expectedTotalPages);
        model.HasPreviousPage.Should().Be(hasPrev);
        model.HasNextPage.Should().Be(hasNext);
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(-5, "0s")]
    [InlineData(9, "9s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1:00")]
    [InlineData(605, "10:05")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    public void FormatDuration_RendersCompactHumanForm(int seconds, string expected)
    {
        Pages.Admin.SessionsModel.FormatDuration(seconds).Should().Be(expected);
    }

    /// <summary>
    /// Constructs a model with a disabled <see cref="Services.SiteStats.VisitSessionReader"/>
    /// and stub dependencies — DI is irrelevant for the pure URL/state helpers exercised here,
    /// none of the tested code paths touch them.
    /// </summary>
    private static Pages.Admin.SessionsModel NewModel()
    {
        var modelLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Pages.Admin.SessionsModel>.Instance;
        var readerLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenRussiaRouter.WebApp.Services.SiteStats.VisitSessionReader>.Instance;
        var reader = new OpenRussiaRouter.WebApp.Services.SiteStats.VisitSessionReader(
            dataSource: null,
            pseudonymizer: null,
            logger: readerLogger);
        var authProvider = new OpenRussiaRouter.WebApp.Services.Auth.AuthProvider(
            client: null!);
        var pseudonymizer = new OpenRussiaRouter.BuildingBlocks.Analytics.UserPseudonymizer(
            new OpenRussiaRouter.BuildingBlocks.Analytics.ProductAnalyticsOptions { IdentitySalt = "test-salt" });
        return new Pages.Admin.SessionsModel(reader, authProvider, pseudonymizer, modelLogger);
    }
}
