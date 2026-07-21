namespace OpenRussiaRouter.ContentService.Domain.Entities;

public sealed record ToolCategory(
    string Id,
    string NameRu,
    string NameEn,
    int SortOrder);
