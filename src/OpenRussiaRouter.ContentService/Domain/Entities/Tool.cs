namespace OpenRussiaRouter.ContentService.Domain.Entities;

/// <summary>
/// A third-party tool (IDE, CLI, chat client, agent framework) that can be pointed at our
/// OpenAI-compatible API. Logo bytes are deliberately NOT part of this record: the catalog
/// reads it on every render, and dragging a bytea through gRPC each time is wasteful.
/// Use <see cref="ToolLogo"/> for the binary.
/// </summary>
public sealed record Tool(
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
    string? LogoContentType = null,
    bool IsPublished = false,
    int SortOrder = 0,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset UpdatedAtUtc = default);

/// <summary>Card-sized projection for the catalog grid — no markdown bodies.</summary>
public sealed record ToolSummary(
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

public sealed record ToolLogo(
    byte[] Bytes,
    string ContentType,
    string DisplayName,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// How an upsert should treat the existing logo. Without this the repository cannot tell
/// "admin did not touch the logo" from "admin cleared the logo" — both arrive as null bytes.
/// </summary>
public enum LogoUpdate
{
    Keep = 0,
    Replace = 1,
    Clear = 2
}
