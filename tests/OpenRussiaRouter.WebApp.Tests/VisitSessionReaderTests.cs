using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Services.SiteStats;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Tests for the parts of <see cref="VisitSessionReader"/> that need no database: the
/// disabled-sink path, the <c>pages</c> jsonb parser and the SQL predicate builder shared by
/// the count and listing queries. Executing the queries against a real Postgres sink is
/// covered the same way as for <see cref="SiteVisitReader"/> — outside this suite.
/// </summary>
public sealed class VisitSessionReaderTests
{
    [Fact]
    public void IsEnabled_WhenNoDataSource_IsFalse()
    {
        var reader = NewDisabledReader();

        reader.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task QueryAsync_WhenDisabled_ReturnsEmptyResult()
    {
        var reader = NewDisabledReader();

        var result = await reader.QueryAsync(new VisitSessionQuery(UserId: "u1"));

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.ResolvedVisitorHash.Should().BeNull();
        result.ResolvedDistinctId.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task QueryAsync_WhenDisabledAndNoFilters_ReturnsEmptyWithoutThrowing(
        string? userId, string? visitorHash)
    {
        var reader = NewDisabledReader();

        var act = () => reader.QueryAsync(new VisitSessionQuery(
            UserId: userId, VisitorHash: visitorHash));

        await act.Should().NotThrowAsync();
        var result = await act();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_WhenDisabled_ReturnsNullWithoutThrowing()
    {
        var reader = NewDisabledReader();

        var act = () => reader.GetByIdAsync(42);

        await act.Should().NotThrowAsync();
        (await act()).Should().BeNull();
    }

    [Fact]
    public void Query_Defaults_HideBotsAndPageBy50()
    {
        var query = new VisitSessionQuery();

        query.IncludeBots.Should().BeFalse();
        query.PageNumber.Should().Be(0);
        query.PageSize.Should().Be(50);
        query.UserId.Should().BeNull();
        query.VisitorHash.Should().BeNull();
        query.FromUtc.Should().BeNull();
        query.ToUtc.Should().BeNull();
    }

    // --- pages jsonb ---------------------------------------------------------------

    [Fact]
    public void ParsePages_ReadsTheShapeWrittenByTheCompactor()
    {
        var reader = NewDisabledReader();
        const string json = """
            [{"t":"2026-07-27T10:00:03.123Z","p":"/models"},{"t":"2026-07-27T10:01:00Z","p":"/models/{id}"}]
            """;

        var pages = reader.ParsePages(json, sessionId: 1);

        pages.Should().HaveCount(2);
        pages[0].Path.Should().Be("/models");
        pages[0].OccurredAt.Should().Be(
            new DateTimeOffset(2026, 7, 27, 10, 0, 3, 123, TimeSpan.Zero));
        pages[1].Path.Should().Be("/models/{id}");
        pages[1].OccurredAt.Should().Be(
            new DateTimeOffset(2026, 7, 27, 10, 1, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void ParsePages_WhenEmptyOrMissing_ReturnsEmptyList(string? json)
    {
        var reader = NewDisabledReader();

        reader.ParsePages(json, sessionId: 1).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"t\":\"2026-07-27T10:00:03Z\"}")] // object instead of the expected array
    public void ParsePages_WhenMalformed_DegradesToEmptyInsteadOfThrowing(string json)
    {
        var reader = NewDisabledReader();

        var act = () => reader.ParsePages(json, sessionId: 7);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    // --- predicate builder ---------------------------------------------------------

    [Fact]
    public void ApplyFilters_ByDefault_ExcludesBotsAndAddsNoParameters()
    {
        var (sql, parameters) = VisitSessionReader.ApplyFilters(
            Anchor, new VisitSessionQuery(), visitorHash: null, new List<Npgsql.NpgsqlParameter>());

        sql.Should().Contain("device_class <> 'bot'");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void ApplyFilters_WhenBotsIncluded_DropsTheDeviceClassPredicate()
    {
        var (sql, _) = VisitSessionReader.ApplyFilters(
            Anchor, new VisitSessionQuery(IncludeBots: true), visitorHash: null, new List<Npgsql.NpgsqlParameter>());

        sql.Should().NotContain("device_class");
    }

    [Fact]
    public void ApplyFilters_WithVisitorHash_ScopesToThatVisitor()
    {
        var (sql, parameters) = VisitSessionReader.ApplyFilters(
            Anchor, new VisitSessionQuery(), visitorHash: "hash-1", new List<Npgsql.NpgsqlParameter>());

        sql.Should().Contain("visitor_hash = :visitorHash");
        parameters.Should().ContainSingle(p => p.ParameterName == "visitorHash")
            .Which.Value.Should().Be("hash-1");
    }

    [Fact]
    public void ApplyFilters_TimeWindow_AppliesToStartedAt()
    {
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);

        var (sql, parameters) = VisitSessionReader.ApplyFilters(
            Anchor,
            new VisitSessionQuery(FromUtc: from, ToUtc: to),
            visitorHash: null,
            new List<Npgsql.NpgsqlParameter>());

        sql.Should().Contain("started_at_utc >= :fromUtc")
            .And.Contain("started_at_utc <= :toUtc");
        // The sessions table has no occurred_at_utc — filtering it would be a runtime error.
        sql.Should().NotContain("occurred_at_utc");
        parameters.Should().Contain(p => p.ParameterName == "fromUtc")
            .And.Contain(p => p.ParameterName == "toUtc");
    }

    /// <summary>Base SQL the reader builds on: every appended clause starts with <c>and</c>.</summary>
    private const string Anchor = "select count(*) from analytics.visit_sessions where 1=1";

    private static VisitSessionReader NewDisabledReader() =>
        new(dataSource: null,
            pseudonymizer: null,
            logger: NullLogger<VisitSessionReader>.Instance);
}
