using OpenRussiaRouter.ContentService.Domain.Entities;

namespace OpenRussiaRouter.ContentService.Application.Abstractions.Persistence;

/// <summary>
/// Two implementations exist and both must stay in sync: <c>ContentRepository</c> (Dapper/Postgres)
/// and <c>InMemoryContentRepository</c> (unit tests, local runs without a connection string).
/// </summary>
public interface IContentRepository
{
    Task<IReadOnlyCollection<ToolSummary>> ListToolSummariesAsync(
        bool publishedOnly,
        string? categoryId,
        CancellationToken ct = default);

    Task<Tool?> GetToolAsync(string id, CancellationToken ct = default);

    Task<ToolLogo?> GetToolLogoAsync(string id, CancellationToken ct = default);

    Task UpsertToolAsync(
        Tool tool,
        LogoUpdate logoUpdate,
        byte[]? logoBytes,
        string? logoContentType,
        CancellationToken ct = default);

    Task DeleteToolAsync(string id, CancellationToken ct = default);

    Task<bool> SetToolPublishedAsync(string id, bool isPublished, DateTimeOffset nowUtc, CancellationToken ct = default);

    Task<IReadOnlyCollection<ToolImage>> ListToolImagesAsync(string toolId, CancellationToken ct = default);

    Task<int> CountToolImagesAsync(string toolId, CancellationToken ct = default);

    Task<ToolImageContent?> GetToolImageAsync(Guid imageId, CancellationToken ct = default);

    Task AddToolImageAsync(ToolImage image, byte[] bytes, CancellationToken ct = default);

    Task<bool> DeleteToolImageAsync(Guid imageId, CancellationToken ct = default);

    Task<IReadOnlyCollection<ToolCategory>> ListToolCategoriesAsync(CancellationToken ct = default);

    Task<ToolCategory?> GetToolCategoryAsync(string id, CancellationToken ct = default);

    Task UpsertToolCategoryAsync(ToolCategory category, DateTimeOffset nowUtc, CancellationToken ct = default);

    Task DeleteToolCategoryAsync(string id, CancellationToken ct = default);
}
