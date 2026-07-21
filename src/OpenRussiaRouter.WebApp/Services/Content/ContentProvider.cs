using System.Globalization;
using Google.Protobuf;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1;

namespace OpenRussiaRouter.WebApp.Services.Content;

/// <summary>
/// Thin gRPC wrapper over ContentService, mirroring <c>BillingProvider</c>: pages never touch the
/// generated client directly, and a failed mutation surfaces as <see cref="ContentException"/>.
/// </summary>
public class ContentProvider(ContentGrpc.ContentService.ContentServiceClient client)
{
    // --- public reads ------------------------------------------------------------------

    // The read methods the public pages depend on are virtual so tests can subclass this
    // provider — same convention as BillingProvider.
    public virtual async Task<IReadOnlyList<ToolSummaryInfo>> ListToolsAsync(
        string? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await client.ListToolsAsync(
            new ContentGrpc.ListToolsRequest { CategoryId = categoryId ?? string.Empty },
            cancellationToken: cancellationToken);
        return response.Tools.Select(ToSummary).ToList();
    }

    public virtual async Task<ToolInfo?> GetToolAsync(string toolId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetToolAsync(
            new ContentGrpc.GetToolRequest { ToolId = toolId },
            cancellationToken: cancellationToken);
        return response is { Found: true, Tool: not null } ? ToTool(response.Tool) : null;
    }

    public async Task<ToolLogoContent?> GetToolLogoAsync(
        string toolId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetToolLogoAsync(
            new ContentGrpc.GetToolLogoRequest { ToolId = toolId },
            cancellationToken: cancellationToken);
        return response.Found && response.LogoBytes.Length > 0
            ? new ToolLogoContent(response.LogoBytes.ToByteArray(), response.LogoContentType, response.DisplayName)
            : null;
    }

    public async Task<ToolImageContent?> GetToolImageAsync(
        Guid imageId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetToolImageAsync(
            new ContentGrpc.GetToolImageRequest { ImageId = imageId.ToString() },
            cancellationToken: cancellationToken);
        return response.Found
            ? new ToolImageContent(response.Bytes.ToByteArray(), response.ContentType, response.FileName)
            : null;
    }

    public virtual async Task<IReadOnlyList<ToolCategoryInfo>> ListToolCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await client.ListToolCategoriesAsync(
            new ContentGrpc.ListToolCategoriesRequest(),
            cancellationToken: cancellationToken);
        return response.Categories
            .Select(c => new ToolCategoryInfo(c.Id, c.NameRu, c.NameEn, c.SortOrder))
            .ToList();
    }

    // --- admin -------------------------------------------------------------------------

    public async Task<IReadOnlyList<ToolSummaryInfo>> ListToolsForAdminAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await client.ListToolsForAdminAsync(
            new ContentGrpc.ListToolsForAdminRequest(),
            cancellationToken: cancellationToken);
        return response.Tools.Select(ToSummary).ToList();
    }

    public async Task<ToolInfo?> GetToolForAdminAsync(
        string toolId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetToolForAdminAsync(
            new ContentGrpc.GetToolRequest { ToolId = toolId },
            cancellationToken: cancellationToken);
        return response is { Found: true, Tool: not null } ? ToTool(response.Tool) : null;
    }

    /// <summary>
    /// Upserts a tool. <paramref name="logoBytes"/> replaces the logo, <paramref name="clearLogo"/>
    /// removes it, and passing neither leaves the stored logo untouched.
    /// </summary>
    public async Task<string> UpsertToolAsync(
        ToolInfo tool,
        byte[]? logoBytes = null,
        string? logoContentType = null,
        bool clearLogo = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ContentGrpc.UpsertToolRequest
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
            LogoContentType = logoContentType ?? string.Empty,
            ClearLogo = clearLogo,
            IsPublished = tool.IsPublished,
            SortOrder = tool.SortOrder
        };
        request.Tags.AddRange(tool.Tags ?? []);

        if (!clearLogo && logoBytes is { Length: > 0 })
        {
            request.LogoBytes = ByteString.CopyFrom(logoBytes);
        }

        var response = await client.UpsertToolAsync(request, cancellationToken: cancellationToken);
        if (!response.Upserted)
        {
            throw new ContentException(response.ErrorCode);
        }

        return response.ToolId;
    }

    public async Task DeleteToolAsync(string toolId, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteToolAsync(
            new ContentGrpc.DeleteToolRequest { ToolId = toolId },
            cancellationToken: cancellationToken);
        if (!response.Deleted)
        {
            throw new ContentException(response.ErrorCode);
        }
    }

    public async Task SetToolPublishedAsync(
        string toolId,
        bool isPublished,
        CancellationToken cancellationToken = default)
    {
        var response = await client.SetToolPublishedAsync(
            new ContentGrpc.SetToolPublishedRequest { ToolId = toolId, IsPublished = isPublished },
            cancellationToken: cancellationToken);
        if (!response.Updated)
        {
            throw new ContentException(response.ErrorCode);
        }
    }

    public async Task<IReadOnlyList<ToolImageInfo>> ListToolImagesAsync(
        string toolId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.ListToolImagesAsync(
            new ContentGrpc.ListToolImagesRequest { ToolId = toolId },
            cancellationToken: cancellationToken);
        return response.Images.Select(i => new ToolImageInfo(
            Guid.TryParse(i.Id, out var id) ? id : Guid.Empty,
            i.ToolId,
            i.FileName,
            i.ContentType,
            i.ByteSize,
            string.IsNullOrEmpty(i.AltText) ? null : i.AltText,
            ParseTimestamp(i.CreatedAtUtc))).ToList();
    }

    public async Task<Guid> UploadToolImageAsync(
        string toolId,
        string fileName,
        string contentType,
        byte[] bytes,
        string? altText = null,
        CancellationToken cancellationToken = default)
    {
        var response = await client.UploadToolImageAsync(new ContentGrpc.UploadToolImageRequest
        {
            ToolId = toolId,
            FileName = fileName,
            ContentType = contentType,
            Bytes = ByteString.CopyFrom(bytes),
            AltText = altText ?? string.Empty
        }, cancellationToken: cancellationToken);

        if (!response.Uploaded)
        {
            throw new ContentException(response.ErrorCode);
        }

        return Guid.Parse(response.ImageId);
    }

    public async Task DeleteToolImageAsync(Guid imageId, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteToolImageAsync(
            new ContentGrpc.DeleteToolImageRequest { ImageId = imageId.ToString() },
            cancellationToken: cancellationToken);
        if (!response.Deleted)
        {
            throw new ContentException(response.ErrorCode);
        }
    }

    public async Task UpsertToolCategoryAsync(
        ToolCategoryInfo category,
        CancellationToken cancellationToken = default)
    {
        var response = await client.UpsertToolCategoryAsync(new ContentGrpc.UpsertToolCategoryRequest
        {
            Id = category.Id,
            NameRu = category.NameRu,
            NameEn = category.NameEn,
            SortOrder = category.SortOrder
        }, cancellationToken: cancellationToken);

        if (!response.Upserted)
        {
            throw new ContentException(response.ErrorCode);
        }
    }

    public async Task DeleteToolCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteToolCategoryAsync(
            new ContentGrpc.DeleteToolCategoryRequest { CategoryId = categoryId },
            cancellationToken: cancellationToken);
        if (!response.Deleted)
        {
            throw new ContentException(response.ErrorCode);
        }
    }

    // --- mapping -----------------------------------------------------------------------

    private static ToolSummaryInfo ToSummary(ContentGrpc.ToolSummary t) => new(
        t.Id,
        t.DisplayName,
        Nullable(t.CategoryId),
        t.Tags.ToList(),
        Nullable(t.TaglineRu),
        Nullable(t.TaglineEn),
        Nullable(t.WebsiteUrl),
        t.HasLogo,
        t.IsPublished,
        t.SortOrder,
        ParseTimestamp(t.UpdatedAtUtc));

    private static ToolInfo ToTool(ContentGrpc.Tool t) => new(
        t.Id,
        t.DisplayName,
        Nullable(t.CategoryId),
        t.Tags.ToList(),
        Nullable(t.WebsiteUrl),
        Nullable(t.TaglineRu),
        Nullable(t.TaglineEn),
        Nullable(t.DescriptionRu),
        Nullable(t.DescriptionEn),
        Nullable(t.SetupRu),
        Nullable(t.SetupEn),
        t.HasLogo,
        t.IsPublished,
        t.SortOrder,
        ParseTimestamp(t.CreatedAtUtc),
        ParseTimestamp(t.UpdatedAtUtc));

    private static string? Nullable(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static DateTimeOffset ParseTimestamp(string value) =>
        string.IsNullOrEmpty(value) ? default : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
