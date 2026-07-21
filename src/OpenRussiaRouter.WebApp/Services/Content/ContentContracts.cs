namespace OpenRussiaRouter.WebApp.Services.Content;

/// <summary>Card-sized tool projection for the catalog grid and the admin list.</summary>
public sealed record ToolSummaryInfo(
    string Id,
    string DisplayName,
    string? CategoryId,
    IReadOnlyList<string> Tags,
    string? TaglineRu,
    string? TaglineEn,
    string? WebsiteUrl,
    bool HasLogo,
    bool IsPublished,
    int SortOrder,
    DateTimeOffset UpdatedAtUtc);

public sealed record ToolInfo(
    string Id,
    string DisplayName,
    string? CategoryId = null,
    IReadOnlyList<string>? Tags = null,
    string? WebsiteUrl = null,
    string? TaglineRu = null,
    string? TaglineEn = null,
    string? DescriptionRu = null,
    string? DescriptionEn = null,
    string? SetupRu = null,
    string? SetupEn = null,
    bool HasLogo = false,
    bool IsPublished = false,
    int SortOrder = 0,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset UpdatedAtUtc = default);

public sealed record ToolLogoContent(byte[] Bytes, string ContentType, string DisplayName);

public sealed record ToolImageInfo(
    Guid Id,
    string ToolId,
    string FileName,
    string ContentType,
    int ByteSize,
    string? AltText,
    DateTimeOffset CreatedAtUtc);

public sealed record ToolImageContent(byte[] Bytes, string ContentType, string FileName);

public sealed record ToolCategoryInfo(string Id, string NameRu, string NameEn, int SortOrder);
