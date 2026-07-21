using OpenRussiaRouter.ContentService.Domain.Entities;

namespace OpenRussiaRouter.ContentService.Infrastructure.Persistence;

public static class ContentPersistenceMapper
{
    public static Tool ToDomain(ToolEntity entity) => new(
        entity.Id,
        entity.DisplayName,
        entity.CategoryId,
        entity.Tags ?? [],
        entity.WebsiteUrl,
        entity.TaglineRu,
        entity.TaglineEn,
        entity.DescriptionRu,
        entity.DescriptionEn,
        entity.SetupRu,
        entity.SetupEn,
        entity.HasLogo,
        entity.LogoContentType,
        entity.IsPublished,
        entity.SortOrder,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc);

    public static ToolSummary ToDomain(ToolSummaryEntity entity) => new(
        entity.Id,
        entity.DisplayName,
        entity.CategoryId,
        entity.Tags ?? [],
        entity.TaglineRu,
        entity.TaglineEn,
        entity.WebsiteUrl,
        entity.HasLogo,
        entity.IsPublished,
        entity.SortOrder,
        entity.UpdatedAtUtc);

    public static ToolLogo? ToDomain(ToolLogoEntity? entity) =>
        entity?.LogoBytes is { Length: > 0 } bytes && !string.IsNullOrEmpty(entity.LogoContentType)
            ? new ToolLogo(bytes, entity.LogoContentType, entity.DisplayName, entity.UpdatedAtUtc)
            : null;

    public static ToolImage ToDomain(ToolImageEntity entity) => new(
        entity.Id,
        entity.ToolId,
        entity.FileName,
        entity.ContentType,
        entity.ByteSize,
        entity.AltText,
        entity.CreatedAtUtc);

    public static ToolImageContent ToDomain(ToolImageContentEntity entity) => new(
        entity.Id,
        entity.Bytes,
        entity.ContentType,
        entity.FileName);

    public static ToolCategory ToDomain(ToolCategoryEntity entity) => new(
        entity.Id,
        entity.NameRu,
        entity.NameEn,
        entity.SortOrder);
}
