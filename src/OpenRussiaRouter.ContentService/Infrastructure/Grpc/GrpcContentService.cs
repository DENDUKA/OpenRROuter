using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ContentService.Application.Exceptions;
using OpenRussiaRouter.ContentService.Application.Services;
using OpenRussiaRouter.ContentService.Domain.Entities;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1;

namespace OpenRussiaRouter.ContentService.Infrastructure.Grpc;

/// <summary>
/// gRPC facade. Validation failures come back as <c>error_code</c> in the response rather than as
/// an RpcException — that is the convention every other service in this repo follows.
/// </summary>
public sealed class GrpcContentService(
    ContentApplicationService content,
    ILogger<GrpcContentService> logger)
    : ContentGrpc.ContentService.ContentServiceBase
{
    // --- public reads ------------------------------------------------------------------

    public override async Task<ContentGrpc.ListToolsResponse> ListTools(
        ContentGrpc.ListToolsRequest request,
        ServerCallContext context)
    {
        var categoryId = string.IsNullOrEmpty(request.CategoryId) ? null : request.CategoryId;

        IReadOnlyCollection<ToolSummary> tools;
        try
        {
            tools = await content.ListPublishedToolsAsync(categoryId, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (ContentValidationException)
        {
            // A malformed category slug in a public query string is a miss, not a server error.
            tools = [];
        }

        var response = new ContentGrpc.ListToolsResponse();
        response.Tools.AddRange(tools.Select(ContentGrpcMapper.ToProto));
        return response;
    }

    public override async Task<ContentGrpc.ListToolsResponse> ListToolsForAdmin(
        ContentGrpc.ListToolsForAdminRequest request,
        ServerCallContext context)
    {
        var tools = await content.ListAllToolsAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new ContentGrpc.ListToolsResponse();
        response.Tools.AddRange(tools.Select(ContentGrpcMapper.ToProto));
        return response;
    }

    public override async Task<ContentGrpc.GetToolResponse> GetTool(
        ContentGrpc.GetToolRequest request,
        ServerCallContext context)
    {
        try
        {
            var tool = await content.GetPublishedToolAsync(request.ToolId, context.CancellationToken)
                .ConfigureAwait(false);
            return tool is null
                ? new ContentGrpc.GetToolResponse { Found = false }
                : new ContentGrpc.GetToolResponse { Found = true, Tool = ContentGrpcMapper.ToProto(tool) };
        }
        catch (ContentValidationException)
        {
            return new ContentGrpc.GetToolResponse { Found = false };
        }
    }

    public override async Task<ContentGrpc.GetToolResponse> GetToolForAdmin(
        ContentGrpc.GetToolRequest request,
        ServerCallContext context)
    {
        try
        {
            var tool = await content.GetToolAsync(request.ToolId, context.CancellationToken)
                .ConfigureAwait(false);
            return tool is null
                ? new ContentGrpc.GetToolResponse { Found = false }
                : new ContentGrpc.GetToolResponse { Found = true, Tool = ContentGrpcMapper.ToProto(tool) };
        }
        catch (ContentValidationException)
        {
            return new ContentGrpc.GetToolResponse { Found = false };
        }
    }

    public override async Task<ContentGrpc.GetToolLogoResponse> GetToolLogo(
        ContentGrpc.GetToolLogoRequest request,
        ServerCallContext context)
    {
        try
        {
            var logo = await content.GetToolLogoAsync(request.ToolId, context.CancellationToken)
                .ConfigureAwait(false);
            if (logo is null)
            {
                return new ContentGrpc.GetToolLogoResponse { Found = false };
            }

            return new ContentGrpc.GetToolLogoResponse
            {
                Found = true,
                LogoBytes = ByteString.CopyFrom(logo.Bytes),
                LogoContentType = logo.ContentType,
                DisplayName = logo.DisplayName,
                UpdatedAtUtc = logo.UpdatedAtUtc.ToString("O")
            };
        }
        catch (ContentValidationException)
        {
            return new ContentGrpc.GetToolLogoResponse { Found = false };
        }
    }

    public override async Task<ContentGrpc.GetToolImageResponse> GetToolImage(
        ContentGrpc.GetToolImageRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ImageId, out var imageId))
        {
            return new ContentGrpc.GetToolImageResponse { Found = false };
        }

        var image = await content.GetToolImageAsync(imageId, context.CancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return new ContentGrpc.GetToolImageResponse { Found = false };
        }

        return new ContentGrpc.GetToolImageResponse
        {
            Found = true,
            Bytes = ByteString.CopyFrom(image.Bytes),
            ContentType = image.ContentType,
            FileName = image.FileName
        };
    }

    public override async Task<ContentGrpc.ListToolCategoriesResponse> ListToolCategories(
        ContentGrpc.ListToolCategoriesRequest request,
        ServerCallContext context)
    {
        var categories = await content.ListToolCategoriesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new ContentGrpc.ListToolCategoriesResponse();
        response.Categories.AddRange(categories.Select(ContentGrpcMapper.ToProto));
        return response;
    }

    // --- admin writes ------------------------------------------------------------------

    public override async Task<ContentGrpc.UpsertToolResponse> UpsertTool(
        ContentGrpc.UpsertToolRequest request,
        ServerCallContext context)
    {
        try
        {
            var logoUpdate = request.ClearLogo
                ? LogoUpdate.Clear
                : request.LogoBytes.Length > 0
                    ? LogoUpdate.Replace
                    : LogoUpdate.Keep;

            var tool = new Tool(
                request.Id,
                request.DisplayName,
                Nullable(request.CategoryId),
                request.Tags.ToArray(),
                Nullable(request.WebsiteUrl),
                Nullable(request.TaglineRu),
                Nullable(request.TaglineEn),
                Nullable(request.DescriptionRu),
                Nullable(request.DescriptionEn),
                Nullable(request.SetupRu),
                Nullable(request.SetupEn),
                HasLogo: false,
                LogoContentType: null,
                IsPublished: request.IsPublished,
                SortOrder: request.SortOrder);

            var id = await content.UpsertToolAsync(
                tool,
                logoUpdate,
                logoUpdate == LogoUpdate.Replace ? request.LogoBytes.ToByteArray() : null,
                Nullable(request.LogoContentType),
                context.CancellationToken).ConfigureAwait(false);

            return new ContentGrpc.UpsertToolResponse { Upserted = true, ToolId = id };
        }
        catch (ContentValidationException ex)
        {
            logger.LogWarning(ex, "UpsertTool rejected. Id: {Id} Code: {Code}", request.Id, ex.ErrorCode);
            return new ContentGrpc.UpsertToolResponse { Upserted = false, ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ContentGrpc.DeleteToolResponse> DeleteTool(
        ContentGrpc.DeleteToolRequest request,
        ServerCallContext context)
    {
        try
        {
            await content.DeleteToolAsync(request.ToolId, context.CancellationToken).ConfigureAwait(false);
            return new ContentGrpc.DeleteToolResponse { Deleted = true };
        }
        catch (ContentValidationException ex)
        {
            logger.LogWarning(ex, "DeleteTool rejected. Id: {Id} Code: {Code}", request.ToolId, ex.ErrorCode);
            return new ContentGrpc.DeleteToolResponse { Deleted = false, ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ContentGrpc.SetToolPublishedResponse> SetToolPublished(
        ContentGrpc.SetToolPublishedRequest request,
        ServerCallContext context)
    {
        try
        {
            await content.SetToolPublishedAsync(request.ToolId, request.IsPublished, context.CancellationToken)
                .ConfigureAwait(false);
            return new ContentGrpc.SetToolPublishedResponse { Updated = true };
        }
        catch (ContentValidationException ex)
        {
            logger.LogWarning(ex, "SetToolPublished rejected. Id: {Id} Code: {Code}", request.ToolId, ex.ErrorCode);
            return new ContentGrpc.SetToolPublishedResponse { Updated = false, ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ContentGrpc.ListToolImagesResponse> ListToolImages(
        ContentGrpc.ListToolImagesRequest request,
        ServerCallContext context)
    {
        var response = new ContentGrpc.ListToolImagesResponse();
        try
        {
            var images = await content.ListToolImagesAsync(request.ToolId, context.CancellationToken)
                .ConfigureAwait(false);
            response.Images.AddRange(images.Select(ContentGrpcMapper.ToProto));
        }
        catch (ContentValidationException)
        {
            // Malformed tool id — an empty list is the honest answer.
        }

        return response;
    }

    public override async Task<ContentGrpc.UploadToolImageResponse> UploadToolImage(
        ContentGrpc.UploadToolImageRequest request,
        ServerCallContext context)
    {
        try
        {
            var imageId = await content.AddToolImageAsync(
                request.ToolId,
                request.FileName,
                request.Bytes.ToByteArray(),
                Nullable(request.AltText),
                context.CancellationToken).ConfigureAwait(false);

            return new ContentGrpc.UploadToolImageResponse { Uploaded = true, ImageId = imageId.ToString() };
        }
        catch (ContentValidationException ex)
        {
            logger.LogWarning(ex, "UploadToolImage rejected. Tool: {Id} Code: {Code}", request.ToolId, ex.ErrorCode);
            return new ContentGrpc.UploadToolImageResponse { Uploaded = false, ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ContentGrpc.DeleteToolImageResponse> DeleteToolImage(
        ContentGrpc.DeleteToolImageRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ImageId, out var imageId))
        {
            return new ContentGrpc.DeleteToolImageResponse
            {
                Deleted = false,
                ErrorCode = ContentErrorCodes.ImageNotFound
            };
        }

        try
        {
            await content.DeleteToolImageAsync(imageId, context.CancellationToken).ConfigureAwait(false);
            return new ContentGrpc.DeleteToolImageResponse { Deleted = true };
        }
        catch (ContentValidationException ex)
        {
            return new ContentGrpc.DeleteToolImageResponse { Deleted = false, ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ContentGrpc.UpsertToolCategoryResponse> UpsertToolCategory(
        ContentGrpc.UpsertToolCategoryRequest request,
        ServerCallContext context)
    {
        try
        {
            var category = new ToolCategory(request.Id, request.NameRu, request.NameEn, request.SortOrder);
            await content.UpsertToolCategoryAsync(category, context.CancellationToken).ConfigureAwait(false);
            return new ContentGrpc.UpsertToolCategoryResponse
            {
                Upserted = true,
                CategoryId = category.Id.Trim().ToLowerInvariant()
            };
        }
        catch (ContentValidationException ex)
        {
            logger.LogWarning(ex, "UpsertToolCategory rejected. Id: {Id} Code: {Code}", request.Id, ex.ErrorCode);
            return new ContentGrpc.UpsertToolCategoryResponse { Upserted = false, ErrorCode = ex.ErrorCode };
        }
    }

    public override async Task<ContentGrpc.DeleteToolCategoryResponse> DeleteToolCategory(
        ContentGrpc.DeleteToolCategoryRequest request,
        ServerCallContext context)
    {
        try
        {
            await content.DeleteToolCategoryAsync(request.CategoryId, context.CancellationToken).ConfigureAwait(false);
            return new ContentGrpc.DeleteToolCategoryResponse { Deleted = true };
        }
        catch (ContentValidationException ex)
        {
            return new ContentGrpc.DeleteToolCategoryResponse { Deleted = false, ErrorCode = ex.ErrorCode };
        }
    }

    private static string? Nullable(string value) => string.IsNullOrEmpty(value) ? null : value;
}
