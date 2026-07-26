using FluentAssertions;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Pure-function tests for the admin visitor-history page. We deliberately cover only the
/// query-string plumbing and datetime parsing here — the database-backed
/// <see cref="Services.SiteStats.SiteVisitReader"/> requires a Postgres sink that is
/// not stood up in this unit suite (Testcontainers-skipped at the suite level).
/// </summary>
public sealed class VisitorsPageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void ParseDate_InvalidOrNull_ReturnsNull(string? value)
    {
        Pages.Admin.VisitorsModel.ParseDate(value).Should().BeNull();
    }

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024-01-15T10:30:00Z")]
    [InlineData("2024-01-15T10:30:00+03:00")]
    public void ParseDate_Valid_ReturnsOffset(string value)
    {
        var parsed = Pages.Admin.VisitorsModel.ParseDate(value);
        parsed.Should().NotBeNull();
        parsed!.Value.Year.Should().Be(2024);
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

    [Fact]
    public void BuildPageUrl_NoFiltersAndFirstPage_ReturnsBarePath()
    {
        var model = NewModel();
        model.BuildPageUrl(0).Should().Be("/admin/visitors");
    }

    [Fact]
    public void BuildPageUrl_PageIndex_IncludesP()
    {
        var model = NewModel();
        model.BuildPageUrl(3).Should().Be("/admin/visitors?p=3");
    }

    [Fact]
    public void BuildPageUrl_AllFilters_EscapedAndOrderedPageFirst()
    {
        var model = NewModel();
        model.UserId = "abc def";
        model.VisitorHash = "h/g";
        model.From = "2024-01-01";
        model.To = "2024-12-31";

        var url = model.BuildPageUrl(2);

        url.Should().StartWith("/admin/visitors?p=2&");
        url.Should().Contain("userId=abc%20def");
        url.Should().Contain("visitorHash=h%2Fg");
        url.Should().Contain("from=2024-01-01");
        url.Should().Contain("to=2024-12-31");
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

    /// <summary>
    /// Constructs a model with a disabled <see cref="SiteVisitReader"/> — DI is irrelevant
    /// for the pure URL/state helpers we exercise here.
    /// </summary>
    private static Pages.Admin.VisitorsModel NewModel()
    {
        var modelLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Pages.Admin.VisitorsModel>.Instance;
        var readerLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenRussiaRouter.WebApp.Services.SiteStats.SiteVisitReader>.Instance;
        var reader = new OpenRussiaRouter.WebApp.Services.SiteStats.SiteVisitReader(
            dataSource: null,
            pseudonymizer: null,
            logger: readerLogger);
        return new Pages.Admin.VisitorsModel(reader, modelLogger);
    }
}