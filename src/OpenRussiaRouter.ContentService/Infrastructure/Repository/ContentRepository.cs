using Dapper;
using OpenRussiaRouter.ContentService.Application.Abstractions.Persistence;
using OpenRussiaRouter.ContentService.Domain.Entities;
using OpenRussiaRouter.ContentService.Infrastructure.Persistence;

namespace OpenRussiaRouter.ContentService.Infrastructure.Repository;

public sealed class ContentRepository(IContentDbConnectionFactory connectionFactory) : IContentRepository
{
    // logo_bytes is deliberately excluded: only GetToolLogoAsync pulls the bytea.
    private const string ToolSelectSql =
        """
        select id as Id, display_name as DisplayName, category_id as CategoryId, tags as Tags,
               website_url as WebsiteUrl, tagline_ru as TaglineRu, tagline_en as TaglineEn,
               description_ru as DescriptionRu, description_en as DescriptionEn,
               setup_ru as SetupRu, setup_en as SetupEn,
               (logo_bytes is not null) as HasLogo, logo_content_type as LogoContentType,
               is_published as IsPublished, sort_order as SortOrder,
               created_at_utc as CreatedAtUtc, updated_at_utc as UpdatedAtUtc
        from content.tools
        """;

    private const string ToolSummarySelectSql =
        """
        select id as Id, display_name as DisplayName, category_id as CategoryId, tags as Tags,
               tagline_ru as TaglineRu, tagline_en as TaglineEn, website_url as WebsiteUrl,
               (logo_bytes is not null) as HasLogo, is_published as IsPublished,
               sort_order as SortOrder, updated_at_utc as UpdatedAtUtc
        from content.tools
        """;

    private const string ToolImageSelectSql =
        """
        select id as Id, tool_id as ToolId, file_name as FileName, content_type as ContentType,
               byte_size as ByteSize, alt_text as AltText, created_at_utc as CreatedAtUtc
        from content.tool_images
        """;

    private const string ToolCategorySelectSql =
        """
        select id as Id, name_ru as NameRu, name_en as NameEn, sort_order as SortOrder
        from content.tool_categories
        """;

    public async Task<IReadOnlyCollection<ToolSummary>> ListToolSummariesAsync(
        bool publishedOnly,
        string? categoryId,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ToolSummaryEntity>(new CommandDefinition(
            ToolSummarySelectSql +
            """
             where (@PublishedOnly = false or is_published = true)
               and (@CategoryId::text is null or category_id = @CategoryId)
             order by sort_order desc, display_name;
            """,
            new { PublishedOnly = publishedOnly, CategoryId = categoryId },
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(ContentPersistenceMapper.ToDomain)
            .ToArray();
        return items;
    }

    public async Task<Tool?> GetToolAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ToolEntity>(new CommandDefinition(
            ToolSelectSql + " where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : ContentPersistenceMapper.ToDomain(entity);
    }

    public async Task<ToolLogo?> GetToolLogoAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ToolLogoEntity>(new CommandDefinition(
            """
            select logo_bytes as LogoBytes, logo_content_type as LogoContentType,
                   display_name as DisplayName, updated_at_utc as UpdatedAtUtc
            from content.tools
            where id = @Id;
            """,
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return ContentPersistenceMapper.ToDomain(entity);
    }

    public async Task UpsertToolAsync(
        Tool tool,
        LogoUpdate logoUpdate,
        byte[]? logoBytes,
        string? logoContentType,
        CancellationToken ct = default)
    {
        var touchLogo = logoUpdate != LogoUpdate.Keep;
        var newLogoBytes = logoUpdate == LogoUpdate.Replace ? logoBytes : null;
        var newLogoType = logoUpdate == LogoUpdate.Replace ? logoContentType : null;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.tools (
                id, display_name, category_id, tags, website_url,
                tagline_ru, tagline_en, description_ru, description_en, setup_ru, setup_en,
                logo_bytes, logo_content_type, is_published, sort_order, created_at_utc, updated_at_utc)
            values (
                @Id, @DisplayName, @CategoryId, @Tags, @WebsiteUrl,
                @TaglineRu, @TaglineEn, @DescriptionRu, @DescriptionEn, @SetupRu, @SetupEn,
                @LogoBytes, @LogoContentType, @IsPublished, @SortOrder, @CreatedAtUtc, @UpdatedAtUtc)
            on conflict (id) do update set
                display_name = excluded.display_name,
                category_id = excluded.category_id,
                tags = excluded.tags,
                website_url = excluded.website_url,
                tagline_ru = excluded.tagline_ru,
                tagline_en = excluded.tagline_en,
                description_ru = excluded.description_ru,
                description_en = excluded.description_en,
                setup_ru = excluded.setup_ru,
                setup_en = excluded.setup_en,
                logo_bytes = case when @TouchLogo then excluded.logo_bytes else content.tools.logo_bytes end,
                logo_content_type = case when @TouchLogo then excluded.logo_content_type else content.tools.logo_content_type end,
                is_published = excluded.is_published,
                sort_order = excluded.sort_order,
                updated_at_utc = excluded.updated_at_utc;
            """,
            new
            {
                tool.Id,
                tool.DisplayName,
                tool.CategoryId,
                Tags = (tool.Tags ?? []).ToArray(),
                tool.WebsiteUrl,
                tool.TaglineRu,
                tool.TaglineEn,
                tool.DescriptionRu,
                tool.DescriptionEn,
                tool.SetupRu,
                tool.SetupEn,
                LogoBytes = newLogoBytes,
                LogoContentType = newLogoType,
                tool.IsPublished,
                tool.SortOrder,
                tool.CreatedAtUtc,
                tool.UpdatedAtUtc,
                TouchLogo = touchLogo
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteToolAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        // tool_images cascades via the FK.
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.tools where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> SetToolPublishedAsync(
        string id,
        bool isPublished,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            update content.tools
            set is_published = @IsPublished, updated_at_utc = @UpdatedAtUtc
            where id = @Id;
            """,
            new { Id = id, IsPublished = isPublished, UpdatedAtUtc = nowUtc },
            cancellationToken: ct)).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<IReadOnlyCollection<ToolImage>> ListToolImagesAsync(string toolId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ToolImageEntity>(new CommandDefinition(
            ToolImageSelectSql + " where tool_id = @ToolId order by created_at_utc desc;",
            new { ToolId = toolId },
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(ContentPersistenceMapper.ToDomain)
            .ToArray();
        return items;
    }

    public async Task<int> CountToolImagesAsync(string toolId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from content.tool_images where tool_id = @ToolId;",
            new { ToolId = toolId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ToolImageContent?> GetToolImageAsync(Guid imageId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ToolImageContentEntity>(new CommandDefinition(
            """
            select id as Id, bytes as Bytes, content_type as ContentType, file_name as FileName
            from content.tool_images
            where id = @Id;
            """,
            new { Id = imageId },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : ContentPersistenceMapper.ToDomain(entity);
    }

    public async Task AddToolImageAsync(ToolImage image, byte[] bytes, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.tool_images (id, tool_id, file_name, content_type, byte_size, bytes, alt_text, created_at_utc)
            values (@Id, @ToolId, @FileName, @ContentType, @ByteSize, @Bytes, @AltText, @CreatedAtUtc);
            """,
            new
            {
                image.Id,
                image.ToolId,
                image.FileName,
                image.ContentType,
                image.ByteSize,
                Bytes = bytes,
                image.AltText,
                image.CreatedAtUtc
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> DeleteToolImageAsync(Guid imageId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.tool_images where id = @Id;",
            new { Id = imageId },
            cancellationToken: ct)).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<IReadOnlyCollection<ToolCategory>> ListToolCategoriesAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ToolCategoryEntity>(new CommandDefinition(
            ToolCategorySelectSql + " order by sort_order desc, name_en;",
            cancellationToken: ct)).ConfigureAwait(false))
            .Select(ContentPersistenceMapper.ToDomain)
            .ToArray();
        return items;
    }

    public async Task<ToolCategory?> GetToolCategoryAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var entity = await connection.QuerySingleOrDefaultAsync<ToolCategoryEntity>(new CommandDefinition(
            ToolCategorySelectSql + " where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
        return entity is null ? null : ContentPersistenceMapper.ToDomain(entity);
    }

    public async Task UpsertToolCategoryAsync(
        ToolCategory category,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.tool_categories (id, name_ru, name_en, sort_order, created_at_utc, updated_at_utc)
            values (@Id, @NameRu, @NameEn, @SortOrder, @NowUtc, @NowUtc)
            on conflict (id) do update set
                name_ru = excluded.name_ru,
                name_en = excluded.name_en,
                sort_order = excluded.sort_order,
                updated_at_utc = excluded.updated_at_utc;
            """,
            new { category.Id, category.NameRu, category.NameEn, category.SortOrder, NowUtc = nowUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteToolCategoryAsync(string id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        // tools.category_id is ON DELETE SET NULL — tools survive as uncategorized.
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.tool_categories where id = @Id;",
            new { Id = id },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}
