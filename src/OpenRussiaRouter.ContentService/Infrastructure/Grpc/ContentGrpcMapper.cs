using OpenRussiaRouter.ContentService.Domain.Entities;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1;

namespace OpenRussiaRouter.ContentService.Infrastructure.Grpc;

/// <summary>
/// Domain → proto mapping. Proto3 has no nullable strings, so nulls become empty strings;
/// timestamps travel as ISO-8601 round-trip ("O") strings, per the repo-wide convention.
/// </summary>
public static class ContentGrpcMapper
{
    public static ContentGrpc.Tool ToProto(Tool tool)
    {
        var message = new ContentGrpc.Tool
        {
            Id = tool.Id,
            DisplayName = tool.DisplayName,
            CategoryId = tool.CategoryId ?? string.Empty,
            WebsiteUrl = tool.WebsiteUrl ?? string.Empty,
            TaglineRu = tool.TaglineRu ?? string.Empty,
            TaglineEn = tool.TaglineEn ?? string.Empty,
            DescriptionRu = tool.DescriptionRu ?? string.Empty,
            DescriptionEn = tool.DescriptionEn ?? string.Empty,
            SetupRu = tool.SetupRu ?? string.Empty,
            SetupEn = tool.SetupEn ?? string.Empty,
            HasLogo = tool.HasLogo,
            LogoContentType = tool.LogoContentType ?? string.Empty,
            IsPublished = tool.IsPublished,
            SortOrder = tool.SortOrder,
            CreatedAtUtc = tool.CreatedAtUtc.ToString("O"),
            UpdatedAtUtc = tool.UpdatedAtUtc.ToString("O")
        };
        message.Tags.AddRange(tool.Tags ?? []);
        return message;
    }

    public static ContentGrpc.ToolSummary ToProto(ToolSummary summary)
    {
        var message = new ContentGrpc.ToolSummary
        {
            Id = summary.Id,
            DisplayName = summary.DisplayName,
            CategoryId = summary.CategoryId ?? string.Empty,
            TaglineRu = summary.TaglineRu ?? string.Empty,
            TaglineEn = summary.TaglineEn ?? string.Empty,
            WebsiteUrl = summary.WebsiteUrl ?? string.Empty,
            HasLogo = summary.HasLogo,
            IsPublished = summary.IsPublished,
            SortOrder = summary.SortOrder,
            UpdatedAtUtc = summary.UpdatedAtUtc.ToString("O")
        };
        message.Tags.AddRange(summary.Tags);
        return message;
    }

    public static ContentGrpc.ToolImage ToProto(ToolImage image) => new()
    {
        Id = image.Id.ToString(),
        ToolId = image.ToolId,
        FileName = image.FileName,
        ContentType = image.ContentType,
        ByteSize = image.ByteSize,
        AltText = image.AltText ?? string.Empty,
        CreatedAtUtc = image.CreatedAtUtc.ToString("O")
    };

    public static ContentGrpc.ToolCategory ToProto(ToolCategory category) => new()
    {
        Id = category.Id,
        NameRu = category.NameRu,
        NameEn = category.NameEn,
        SortOrder = category.SortOrder
    };
}
