namespace OpenRussiaRouter.ContentService.Domain.Entities;

/// <summary>Metadata of an image embedded into a tool's markdown. Bytes live in <see cref="ToolImageContent"/>.</summary>
public sealed record ToolImage(
    Guid Id,
    string ToolId,
    string FileName,
    string ContentType,
    int ByteSize,
    string? AltText,
    DateTimeOffset CreatedAtUtc);

public sealed record ToolImageContent(
    Guid Id,
    byte[] Bytes,
    string ContentType,
    string FileName);
