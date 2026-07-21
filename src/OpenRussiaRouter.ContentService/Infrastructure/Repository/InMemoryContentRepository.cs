using OpenRussiaRouter.ContentService.Application.Abstractions.Persistence;
using OpenRussiaRouter.ContentService.Domain.Entities;

namespace OpenRussiaRouter.ContentService.Infrastructure.Repository;

/// <summary>
/// In-memory twin of <see cref="ContentRepository"/>: used by unit tests and by local runs with
/// no connection string. Every method added to <see cref="IContentRepository"/> must land here too.
/// </summary>
public sealed class InMemoryContentRepository : IContentRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Tool> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _logos = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (ToolImage Meta, byte[] Bytes)> _images = [];
    private readonly Dictionary<string, ToolCategory> _categories = new(StringComparer.Ordinal);

    public Task<IReadOnlyCollection<ToolSummary>> ListToolSummariesAsync(
        bool publishedOnly,
        string? categoryId,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyCollection<ToolSummary> result = _tools.Values
                .Where(t => !publishedOnly || t.IsPublished)
                .Where(t => categoryId is null || string.Equals(t.CategoryId, categoryId, StringComparison.Ordinal))
                .OrderByDescending(t => t.SortOrder)
                .ThenBy(t => t.DisplayName, StringComparer.Ordinal)
                .Select(t => new ToolSummary(
                    t.Id, t.DisplayName, t.CategoryId, t.Tags ?? [], t.TaglineRu, t.TaglineEn,
                    t.WebsiteUrl, t.HasLogo, t.IsPublished, t.SortOrder, t.UpdatedAtUtc))
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<Tool?> GetToolAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tools.TryGetValue(id, out var tool) ? tool : null);
        }
    }

    public Task<ToolLogo?> GetToolLogoAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_tools.TryGetValue(id, out var tool) || !_logos.TryGetValue(id, out var logo))
            {
                return Task.FromResult<ToolLogo?>(null);
            }

            return Task.FromResult<ToolLogo?>(
                new ToolLogo(logo.Bytes, logo.ContentType, tool.DisplayName, tool.UpdatedAtUtc));
        }
    }

    public Task UpsertToolAsync(
        Tool tool,
        LogoUpdate logoUpdate,
        byte[]? logoBytes,
        string? logoContentType,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            switch (logoUpdate)
            {
                case LogoUpdate.Replace when logoBytes is { Length: > 0 } && logoContentType is not null:
                    _logos[tool.Id] = (logoBytes, logoContentType);
                    break;
                case LogoUpdate.Clear:
                    _logos.Remove(tool.Id);
                    break;
                case LogoUpdate.Keep:
                default:
                    break;
            }

            var hasLogo = _logos.TryGetValue(tool.Id, out var stored);
            _tools[tool.Id] = tool with
            {
                HasLogo = hasLogo,
                LogoContentType = hasLogo ? stored.ContentType : null
            };
            return Task.CompletedTask;
        }
    }

    public Task DeleteToolAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _tools.Remove(id);
            _logos.Remove(id);

            // Mirrors ON DELETE CASCADE on tool_images.tool_id.
            foreach (var imageId in _images
                         .Where(kv => string.Equals(kv.Value.Meta.ToolId, id, StringComparison.Ordinal))
                         .Select(kv => kv.Key)
                         .ToArray())
            {
                _images.Remove(imageId);
            }

            return Task.CompletedTask;
        }
    }

    public Task<bool> SetToolPublishedAsync(
        string id,
        bool isPublished,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_tools.TryGetValue(id, out var tool))
            {
                return Task.FromResult(false);
            }

            _tools[id] = tool with { IsPublished = isPublished, UpdatedAtUtc = nowUtc };
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyCollection<ToolImage>> ListToolImagesAsync(string toolId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyCollection<ToolImage> result = _images.Values
                .Where(i => string.Equals(i.Meta.ToolId, toolId, StringComparison.Ordinal))
                .Select(i => i.Meta)
                .OrderByDescending(i => i.CreatedAtUtc)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<int> CountToolImagesAsync(string toolId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_images.Values
                .Count(i => string.Equals(i.Meta.ToolId, toolId, StringComparison.Ordinal)));
        }
    }

    public Task<ToolImageContent?> GetToolImageAsync(Guid imageId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_images.TryGetValue(imageId, out var image))
            {
                return Task.FromResult<ToolImageContent?>(null);
            }

            return Task.FromResult<ToolImageContent?>(new ToolImageContent(
                image.Meta.Id, image.Bytes, image.Meta.ContentType, image.Meta.FileName));
        }
    }

    public Task AddToolImageAsync(ToolImage image, byte[] bytes, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _images[image.Id] = (image, bytes);
            return Task.CompletedTask;
        }
    }

    public Task<bool> DeleteToolImageAsync(Guid imageId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_images.Remove(imageId));
        }
    }

    public Task<IReadOnlyCollection<ToolCategory>> ListToolCategoriesAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyCollection<ToolCategory> result = _categories.Values
                .OrderByDescending(c => c.SortOrder)
                .ThenBy(c => c.NameEn, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<ToolCategory?> GetToolCategoryAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_categories.TryGetValue(id, out var category) ? category : null);
        }
    }

    public Task UpsertToolCategoryAsync(
        ToolCategory category,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            _categories[category.Id] = category;
            return Task.CompletedTask;
        }
    }

    public Task DeleteToolCategoryAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _categories.Remove(id);

            // Mirrors ON DELETE SET NULL on tools.category_id.
            foreach (var toolId in _tools
                         .Where(kv => string.Equals(kv.Value.CategoryId, id, StringComparison.Ordinal))
                         .Select(kv => kv.Key)
                         .ToArray())
            {
                _tools[toolId] = _tools[toolId] with { CategoryId = null };
            }

            return Task.CompletedTask;
        }
    }
}
