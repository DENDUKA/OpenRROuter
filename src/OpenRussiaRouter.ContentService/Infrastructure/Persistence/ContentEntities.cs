namespace OpenRussiaRouter.ContentService.Infrastructure.Persistence;

/// <summary>Dapper row types. Mutable by necessity — Dapper materializes via property setters.</summary>
public sealed class ToolEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? CategoryId { get; set; }
    public string[]? Tags { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? TaglineRu { get; set; }
    public string? TaglineEn { get; set; }
    public string? DescriptionRu { get; set; }
    public string? DescriptionEn { get; set; }
    public string? SetupRu { get; set; }
    public string? SetupEn { get; set; }
    public bool HasLogo { get; set; }
    public string? LogoContentType { get; set; }
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ToolSummaryEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? CategoryId { get; set; }
    public string[]? Tags { get; set; }
    public string? TaglineRu { get; set; }
    public string? TaglineEn { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool HasLogo { get; set; }
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ToolLogoEntity
{
    public byte[]? LogoBytes { get; set; }
    public string? LogoContentType { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ToolImageEntity
{
    public Guid Id { get; set; }
    public string ToolId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int ByteSize { get; set; }
    public string? AltText { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ToolImageContentEntity
{
    public Guid Id { get; set; }
    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = "";
    public string FileName { get; set; } = "";
}

public sealed class ToolCategoryEntity
{
    public string Id { get; set; } = "";
    public string NameRu { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int SortOrder { get; set; }
}
