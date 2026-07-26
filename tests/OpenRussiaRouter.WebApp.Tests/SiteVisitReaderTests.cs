using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.SiteStats;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Tests for the disabled-sink path of <see cref="SiteVisitReader"/>. The query execution
/// against a real Postgres sink is covered by integration tests (Testcontainers-skipped at
/// this suite's level), same as the write-side flush worker.
/// </summary>
public sealed class SiteVisitReaderTests
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

        var result = await reader.QueryAsync(new SiteVisitQuery(UserId: "u1"));

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

        var act = () => reader.QueryAsync(new SiteVisitQuery(
            UserId: userId, VisitorHash: visitorHash));

        await act.Should().NotThrowAsync();
        var result = await act();
        result.Items.Should().BeEmpty();
    }

    private static SiteVisitReader NewDisabledReader() =>
        new(dataSource: null,
            pseudonymizer: null,
            logger: NullLogger<SiteVisitReader>.Instance);
}