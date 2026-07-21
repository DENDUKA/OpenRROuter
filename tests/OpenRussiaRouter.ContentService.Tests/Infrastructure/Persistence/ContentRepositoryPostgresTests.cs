using System.Data;
using FluentAssertions;
using Npgsql;
using OpenRussiaRouter.ContentService.Domain.Entities;
using OpenRussiaRouter.ContentService.Infrastructure.Persistence;
using OpenRussiaRouter.ContentService.Infrastructure.Repository;

namespace OpenRussiaRouter.ContentService.Tests.Infrastructure.Persistence;

[Trait("Category", "PostgreSQL")]
public sealed class ContentRepositoryPostgresTests : IAsyncLifetime
{
    private PostgresTestDatabase? database;

    public async Task InitializeAsync()
    {
        database = await PostgresTestDatabase.StartAsync();
        database?.ApplyMigrations();
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    private sealed class TestConnectionFactory(string connectionString) : IContentDbConnectionFactory
    {
        public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
    }

    private ContentRepository? CreateRepository()
        => database is null ? null : new ContentRepository(new TestConnectionFactory(database.ConnectionString));

    private static byte[] PngBytes(int totalLength = 32)
    {
        var bytes = new byte[totalLength];
        ReadOnlySpan<byte> header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        header.CopyTo(bytes);
        return bytes;
    }

    private static Tool NewTool(string id, bool published = false, int sortOrder = 0) => new(
        id,
        $"Tool {id}",
        CategoryId: null,
        Tags: ["ide", "free"],
        WebsiteUrl: "https://example.com",
        TaglineRu: "кратко",
        TaglineEn: "short",
        DescriptionRu: "## Описание",
        DescriptionEn: "## Description",
        SetupRu: "## Подключение",
        SetupEn: "## Setup",
        HasLogo: false,
        LogoContentType: null,
        IsPublished: published,
        SortOrder: sortOrder,
        CreatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public async Task UpsertAndGet_RoundTripsAllFieldsIncludingTagsArray()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return; // Docker unavailable.
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Keep, null, null);

        var stored = await repository.GetToolAsync("cline");

        stored.Should().NotBeNull();
        stored!.DisplayName.Should().Be("Tool cline");
        stored.Tags.Should().BeEquivalentTo(["ide", "free"]);
        stored.SetupRu.Should().Be("## Подключение");
        stored.HasLogo.Should().BeFalse();
    }

    [Fact]
    public async Task GetTool_NeverReturnsLogoBytes_ButReportsHasLogo()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Replace, PngBytes(), "image/png");

        var stored = await repository.GetToolAsync("cline");
        var logo = await repository.GetToolLogoAsync("cline");

        stored!.HasLogo.Should().BeTrue();
        stored.LogoContentType.Should().Be("image/png");
        logo!.Bytes.Should().HaveCount(32);
    }

    [Fact]
    public async Task UpsertWithLogoKeep_DoesNotWipeExistingLogo()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Replace, PngBytes(), "image/png");

        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Keep, null, null);

        (await repository.GetToolLogoAsync("cline")).Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertWithLogoClear_RemovesLogo()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Replace, PngBytes(), "image/png");

        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Clear, null, null);

        (await repository.GetToolLogoAsync("cline")).Should().BeNull();
    }

    [Fact]
    public async Task ListToolSummaries_PublishedOnly_ExcludesDraftsAndSortsBySortOrderDesc()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("low", published: true, sortOrder: 1), LogoUpdate.Keep, null, null);
        await repository.UpsertToolAsync(NewTool("high", published: true, sortOrder: 50), LogoUpdate.Keep, null, null);
        await repository.UpsertToolAsync(NewTool("draft"), LogoUpdate.Keep, null, null);

        var published = await repository.ListToolSummariesAsync(publishedOnly: true, categoryId: null);
        var all = await repository.ListToolSummariesAsync(publishedOnly: false, categoryId: null);

        published.Select(t => t.Id).Should().Equal("high", "low");
        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListToolSummaries_FiltersByCategory()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        // 'ide' and 'cli' are seeded by 001_initial.sql.
        await repository.UpsertToolAsync(
            NewTool("cline", published: true) with { CategoryId = "ide" }, LogoUpdate.Keep, null, null);
        await repository.UpsertToolAsync(
            NewTool("aider", published: true) with { CategoryId = "cli" }, LogoUpdate.Keep, null, null);

        var ides = await repository.ListToolSummariesAsync(publishedOnly: true, categoryId: "ide");

        ides.Select(t => t.Id).Should().Equal("cline");
    }

    [Fact]
    public async Task DeleteTool_CascadesToItsImages()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Keep, null, null);
        var image = new ToolImage(
            Guid.NewGuid(), "cline", "a.png", "image/png", 32, "alt", DateTimeOffset.UtcNow);
        await repository.AddToolImageAsync(image, PngBytes());

        await repository.DeleteToolAsync("cline");

        (await repository.GetToolImageAsync(image.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteCategory_SetsToolCategoryToNull_RatherThanDeletingTheTool()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolCategoryAsync(
            new ToolCategory("temp-cat", "Временная", "Temporary", 5), DateTimeOffset.UtcNow);
        await repository.UpsertToolAsync(
            NewTool("cline") with { CategoryId = "temp-cat" }, LogoUpdate.Keep, null, null);

        await repository.DeleteToolCategoryAsync("temp-cat");

        var tool = await repository.GetToolAsync("cline");
        tool.Should().NotBeNull();
        tool!.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task ImageCheckConstraint_RejectsDisallowedContentType()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();
        await repository.UpsertToolAsync(NewTool("cline"), LogoUpdate.Keep, null, null);
        var svg = new ToolImage(
            Guid.NewGuid(), "cline", "x.svg", "image/svg+xml", 32, null, DateTimeOffset.UtcNow);

        // The DB is the last line of defence even if the application layer is bypassed.
        var act = () => repository.AddToolImageAsync(svg, PngBytes());

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task SetToolPublished_ReturnsFalseForMissingTool()
    {
        var repository = CreateRepository();
        if (repository is null)
        {
            return;
        }

        database!.Truncate();

        var updated = await repository.SetToolPublishedAsync("ghost", true, DateTimeOffset.UtcNow);

        updated.Should().BeFalse();
    }
}
