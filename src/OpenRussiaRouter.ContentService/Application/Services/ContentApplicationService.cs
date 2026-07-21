using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ContentService.Application.Abstractions.Persistence;
using OpenRussiaRouter.ContentService.Application.Exceptions;
using OpenRussiaRouter.ContentService.Application.Options;
using OpenRussiaRouter.ContentService.Domain.Entities;

namespace OpenRussiaRouter.ContentService.Application.Services;

/// <summary>
/// Facade over <see cref="IContentRepository"/>. Owns every validation rule; the gRPC layer only
/// maps messages. Limits here mirror the DB check constraints in
/// <c>db/migrations/content/001_initial.sql</c> so a bug in one layer cannot corrupt the table.
/// </summary>
public sealed partial class ContentApplicationService(
    IContentRepository repository,
    IOptions<ContentLimitsOptions> limitsOptions,
    TimeProvider timeProvider)
{
    private static readonly string[] AllowedLogoTypes =
        ["image/png", "image/jpeg", "image/webp", "image/svg+xml"];

    // svg is intentionally absent: body images are served from our own origin.
    private static readonly string[] AllowedImageTypes =
        ["image/png", "image/jpeg", "image/webp", "image/gif"];

    private readonly ContentLimitsOptions limits = limitsOptions.Value;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._/-]*$")]
    private static partial Regex SlugRegex();

    // --- tools -------------------------------------------------------------------------

    public Task<IReadOnlyCollection<ToolSummary>> ListPublishedToolsAsync(
        string? categoryId,
        CancellationToken ct = default)
        => repository.ListToolSummariesAsync(
            publishedOnly: true,
            categoryId: NormalizeOptionalSlug(categoryId),
            ct);

    public Task<IReadOnlyCollection<ToolSummary>> ListAllToolsAsync(CancellationToken ct = default)
        => repository.ListToolSummariesAsync(publishedOnly: false, categoryId: null, ct);

    /// <summary>Public read: an unpublished tool is reported as missing.</summary>
    public async Task<Tool?> GetPublishedToolAsync(string id, CancellationToken ct = default)
    {
        var tool = await repository.GetToolAsync(NormalizeSlug(id), ct).ConfigureAwait(false);
        return tool is { IsPublished: true } ? tool : null;
    }

    public Task<Tool?> GetToolAsync(string id, CancellationToken ct = default)
        => repository.GetToolAsync(NormalizeSlug(id), ct);

    public async Task<ToolLogo?> GetToolLogoAsync(string id, CancellationToken ct = default)
        => await repository.GetToolLogoAsync(NormalizeSlug(id), ct).ConfigureAwait(false);

    public async Task<string> UpsertToolAsync(
        Tool tool,
        LogoUpdate logoUpdate,
        byte[]? logoBytes,
        string? logoContentType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var id = NormalizeSlug(tool.Id);
        var displayName = (tool.DisplayName ?? string.Empty).Trim();

        if (displayName.Length == 0)
        {
            throw new ContentValidationException(
                ContentErrorCodes.InvalidDisplayName, "Display name is required.");
        }

        if (displayName.Length > limits.MaxDisplayNameChars)
        {
            throw new ContentValidationException(
                ContentErrorCodes.InvalidDisplayName,
                $"Display name cannot be longer than {limits.MaxDisplayNameChars} characters.");
        }

        var websiteUrl = NormalizeWebsiteUrl(tool.WebsiteUrl);
        var categoryId = NormalizeOptionalSlug(tool.CategoryId);

        if (categoryId is not null)
        {
            var category = await repository.GetToolCategoryAsync(categoryId, ct).ConfigureAwait(false);
            if (category is null)
            {
                throw new ContentValidationException(
                    ContentErrorCodes.UnknownCategory, $"Category '{categoryId}' does not exist.");
            }
        }

        var tagline = new[] { tool.TaglineRu, tool.TaglineEn };
        foreach (var value in tagline)
        {
            EnsureMaxLength(value, limits.MaxTaglineChars, "Tagline");
        }

        foreach (var value in new[] { tool.DescriptionRu, tool.DescriptionEn, tool.SetupRu, tool.SetupEn })
        {
            EnsureMaxLength(value, limits.MaxMarkdownChars, "Markdown text");
        }

        string? resolvedLogoType = null;
        if (logoUpdate == LogoUpdate.Replace)
        {
            resolvedLogoType = ValidateLogo(logoBytes, logoContentType);
        }

        var now = timeProvider.GetUtcNow();
        var existing = await repository.GetToolAsync(id, ct).ConfigureAwait(false);

        var normalized = tool with
        {
            Id = id,
            DisplayName = displayName,
            CategoryId = categoryId,
            Tags = NormalizeTags(tool.Tags),
            WebsiteUrl = websiteUrl,
            TaglineRu = Trimmed(tool.TaglineRu),
            TaglineEn = Trimmed(tool.TaglineEn),
            DescriptionRu = Trimmed(tool.DescriptionRu),
            DescriptionEn = Trimmed(tool.DescriptionEn),
            SetupRu = Trimmed(tool.SetupRu),
            SetupEn = Trimmed(tool.SetupEn),
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        };

        await repository
            .UpsertToolAsync(normalized, logoUpdate, logoBytes, resolvedLogoType, ct)
            .ConfigureAwait(false);

        return id;
    }

    public Task DeleteToolAsync(string id, CancellationToken ct = default)
        => repository.DeleteToolAsync(NormalizeSlug(id), ct);

    public async Task SetToolPublishedAsync(string id, bool isPublished, CancellationToken ct = default)
    {
        var updated = await repository
            .SetToolPublishedAsync(NormalizeSlug(id), isPublished, timeProvider.GetUtcNow(), ct)
            .ConfigureAwait(false);

        if (!updated)
        {
            throw new ContentValidationException(ContentErrorCodes.ToolNotFound, $"Tool '{id}' not found.");
        }
    }

    // --- images ------------------------------------------------------------------------

    public Task<IReadOnlyCollection<ToolImage>> ListToolImagesAsync(string toolId, CancellationToken ct = default)
        => repository.ListToolImagesAsync(NormalizeSlug(toolId), ct);

    public Task<ToolImageContent?> GetToolImageAsync(Guid imageId, CancellationToken ct = default)
        => repository.GetToolImageAsync(imageId, ct);

    public async Task<Guid> AddToolImageAsync(
        string toolId,
        string fileName,
        byte[] bytes,
        string? altText,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var id = NormalizeSlug(toolId);

        var tool = await repository.GetToolAsync(id, ct).ConfigureAwait(false);
        if (tool is null)
        {
            throw new ContentValidationException(ContentErrorCodes.ToolNotFound, $"Tool '{toolId}' not found.");
        }

        if (bytes.Length == 0)
        {
            throw new ContentValidationException(ContentErrorCodes.ImageTooLarge, "Image is empty.");
        }

        if (bytes.Length > limits.MaxImageBytes)
        {
            throw new ContentValidationException(
                ContentErrorCodes.ImageTooLarge,
                $"Image must be at most {limits.MaxImageBytes / 1024} KiB.");
        }

        // Trust the bytes, not the caller's declared content type.
        var detected = ImageContentTypeDetector.Detect(bytes);
        if (detected is null || Array.IndexOf(AllowedImageTypes, detected) < 0)
        {
            throw new ContentValidationException(
                ContentErrorCodes.ImageTypeNotAllowed, "Image must be PNG, JPEG, WebP, or GIF.");
        }

        var count = await repository.CountToolImagesAsync(id, ct).ConfigureAwait(false);
        if (count >= limits.MaxImagesPerTool)
        {
            throw new ContentValidationException(
                ContentErrorCodes.ImageLimitReached,
                $"A tool cannot have more than {limits.MaxImagesPerTool} images.");
        }

        var image = new ToolImage(
            Guid.NewGuid(),
            id,
            SanitizeFileName(fileName),
            detected,
            bytes.Length,
            Trimmed(altText),
            timeProvider.GetUtcNow());

        await repository.AddToolImageAsync(image, bytes, ct).ConfigureAwait(false);
        return image.Id;
    }

    public async Task DeleteToolImageAsync(Guid imageId, CancellationToken ct = default)
    {
        var deleted = await repository.DeleteToolImageAsync(imageId, ct).ConfigureAwait(false);
        if (!deleted)
        {
            throw new ContentValidationException(ContentErrorCodes.ImageNotFound, "Image not found.");
        }
    }

    // --- categories --------------------------------------------------------------------

    public Task<IReadOnlyCollection<ToolCategory>> ListToolCategoriesAsync(CancellationToken ct = default)
        => repository.ListToolCategoriesAsync(ct);

    public Task UpsertToolCategoryAsync(ToolCategory category, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        var id = NormalizeSlug(category.Id);
        var nameRu = (category.NameRu ?? string.Empty).Trim();
        var nameEn = (category.NameEn ?? string.Empty).Trim();

        if (nameRu.Length == 0 || nameEn.Length == 0)
        {
            throw new ContentValidationException(
                ContentErrorCodes.InvalidDisplayName, "Both Russian and English category names are required.");
        }

        EnsureMaxLength(nameRu, limits.MaxDisplayNameChars, "Category name");
        EnsureMaxLength(nameEn, limits.MaxDisplayNameChars, "Category name");

        return repository.UpsertToolCategoryAsync(
            category with { Id = id, NameRu = nameRu, NameEn = nameEn },
            timeProvider.GetUtcNow(),
            ct);
    }

    /// <summary>
    /// Deleting a category does not delete its tools — the FK is <c>ON DELETE SET NULL</c>,
    /// so they fall into the "uncategorized" bucket on /tools.
    /// </summary>
    public Task DeleteToolCategoryAsync(string id, CancellationToken ct = default)
        => repository.DeleteToolCategoryAsync(NormalizeSlug(id), ct);

    // --- helpers -----------------------------------------------------------------------

    private string ValidateLogo(byte[]? logoBytes, string? logoContentType)
    {
        if (logoBytes is not { Length: > 0 })
        {
            throw new ContentValidationException(ContentErrorCodes.LogoTypeNotAllowed, "Logo is empty.");
        }

        if (logoBytes.Length > limits.MaxLogoBytes)
        {
            throw new ContentValidationException(
                ContentErrorCodes.LogoTooLarge, $"Logo must be at most {limits.MaxLogoBytes / 1024} KiB.");
        }

        var declared = (logoContentType ?? string.Empty).Trim().ToLowerInvariant();
        if (Array.IndexOf(AllowedLogoTypes, declared) < 0)
        {
            throw new ContentValidationException(
                ContentErrorCodes.LogoTypeNotAllowed, "Logo must be PNG, JPEG, WebP, or SVG.");
        }

        // SVG has no magic number worth checking; raster formats must match their bytes.
        if (declared != "image/svg+xml")
        {
            var detected = ImageContentTypeDetector.Detect(logoBytes);
            if (detected is null || !string.Equals(detected, declared, StringComparison.Ordinal))
            {
                throw new ContentValidationException(
                    ContentErrorCodes.LogoTypeNotAllowed, "Logo content does not match its declared type.");
            }
        }

        return declared;
    }

    private string NormalizeSlug(string? value)
    {
        var slug = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (slug.Length == 0 || slug.Length > limits.MaxIdChars || !SlugRegex().IsMatch(slug))
        {
            throw new ContentValidationException(
                ContentErrorCodes.InvalidId,
                $"ID must be lowercase alphanumeric with dashes, underscores, dots or slashes, up to {limits.MaxIdChars} characters.");
        }

        return slug;
    }

    private string? NormalizeOptionalSlug(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : NormalizeSlug(value);

    private string? NormalizeWebsiteUrl(string? value)
    {
        var url = Trimmed(value);
        if (url is null)
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ContentValidationException(
                ContentErrorCodes.InvalidUrl, "Website URL must be an absolute http(s) URL.");
        }

        return url;
    }

    private void EnsureMaxLength(string? value, int maxChars, string label)
    {
        if (value is not null && value.Length > maxChars)
        {
            throw new ContentValidationException(
                ContentErrorCodes.TextTooLong, $"{label} cannot be longer than {maxChars} characters.");
        }
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
        => tags is null
            ? []
            : tags.Select(t => (t ?? string.Empty).Trim().ToLowerInvariant())
                .Where(t => t.Length is > 0 and <= 50)
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToArray();

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (name.Length == 0)
        {
            return "image";
        }

        var cleaned = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return cleaned.Length == 0 ? "image" : cleaned[..Math.Min(cleaned.Length, 120)];
    }
}
