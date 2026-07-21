using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Content;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ToolEditModel(ContentProvider contentProvider, ILogger<ToolEditModel> logger) : PageModelBase
{
    private static readonly string[] AllowedLogoTypes =
        ["image/png", "image/jpeg", "image/webp", "image/svg+xml"];

    // svg is not accepted for body images: they are served from our own origin.
    private static readonly string[] AllowedImageTypes =
        ["image/png", "image/jpeg", "image/webp", "image/gif"];

    private const int MaxLogoBytes = 256 * 1024;
    private const int MaxImageBytes = 1024 * 1024;

    public bool IsNew { get; private set; } = true;
    public string? ExistingLogoUrl { get; private set; }
    public IReadOnlyList<ToolCategoryInfo> Categories { get; private set; } = [];
    public IReadOnlyList<ToolImageInfo> Images { get; private set; } = [];

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    [BindProperty] public ToolFormInput Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        if (TempData["SuccessMessage"] is string success)
        {
            SuccessMessage = success;
        }

        await LoadCategoriesAsync();

        if (string.IsNullOrWhiteSpace(id))
        {
            return Page();
        }

        var tool = await contentProvider.GetToolForAdminAsync(id, HttpContext.RequestAborted);
        if (tool is null)
        {
            return NotFound();
        }

        IsNew = false;
        Form = new ToolFormInput
        {
            Id = tool.Id,
            DisplayName = tool.DisplayName,
            CategoryId = tool.CategoryId,
            Tags = string.Join(", ", tool.Tags ?? []),
            WebsiteUrl = tool.WebsiteUrl,
            TaglineRu = tool.TaglineRu,
            TaglineEn = tool.TaglineEn,
            DescriptionRu = tool.DescriptionRu,
            DescriptionEn = tool.DescriptionEn,
            SetupRu = tool.SetupRu,
            SetupEn = tool.SetupEn,
            IsPublished = tool.IsPublished,
            SortOrder = tool.SortOrder
        };

        if (tool.HasLogo)
        {
            ExistingLogoUrl = $"/tools/logo/{Uri.EscapeDataString(tool.Id)}?v={tool.UpdatedAtUtc.UtcDateTime.Ticks}";
        }

        await LoadImagesAsync(tool.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? id)
    {
        IsNew = string.IsNullOrWhiteSpace(id);

        if (!ModelState.IsValid)
        {
            return await RedisplayAsync();
        }

        byte[]? logoBytes = null;
        string? logoContentType = null;
        var clearLogo = Request.Form.ContainsKey("clearLogo");

        var logoFile = Request.Form.Files.GetFile("logoFile");
        if (logoFile is { Length: > 0 })
        {
            if (!AllowedLogoTypes.Contains(logoFile.ContentType))
            {
                ModelState.AddModelError("logoFile", "Logo must be PNG, JPEG, WebP, or SVG.");
                return await RedisplayAsync();
            }

            if (logoFile.Length > MaxLogoBytes)
            {
                ModelState.AddModelError("logoFile", "Logo must be at most 256 KiB.");
                return await RedisplayAsync();
            }

            using var buffer = new MemoryStream();
            await logoFile.CopyToAsync(buffer, HttpContext.RequestAborted);
            logoBytes = buffer.ToArray();
            logoContentType = logoFile.ContentType;
        }

        try
        {
            var tool = new ToolInfo(
                Form.Id.Trim(),
                Form.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(Form.CategoryId) ? null : Form.CategoryId,
                ParseTags(Form.Tags),
                Trimmed(Form.WebsiteUrl),
                Trimmed(Form.TaglineRu),
                Trimmed(Form.TaglineEn),
                Trimmed(Form.DescriptionRu),
                Trimmed(Form.DescriptionEn),
                Trimmed(Form.SetupRu),
                Trimmed(Form.SetupEn),
                HasLogo: false,
                IsPublished: Form.IsPublished,
                SortOrder: Form.SortOrder);

            var storedId = await contentProvider.UpsertToolAsync(
                tool, logoBytes, logoContentType, clearLogo, HttpContext.RequestAborted);

            // Stay on the editor: images need a persisted tool_id (FK), so a brand-new tool has to
            // round-trip through Save once before its image panel unlocks.
            TempData["SuccessMessage"] = IsNew ? "Tool created as a draft." : "Tool saved.";
            return RedirectToPage(new { id = storedId });
        }
        catch (ContentException ex)
        {
            logger.LogWarning(ex, "Tool upsert rejected. Id={Id} Code={Code}", Form.Id, ex.ErrorCode);
            ErrorMessage = DescribeError(ex.ErrorCode);
            return await RedisplayAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save tool. Id={Id}", Form.Id);
            ErrorMessage = "Failed to save the tool.";
            return await RedisplayAsync();
        }
    }

    /// <summary>
    /// AJAX endpoint used by the markdown editor. Returns the markdown snippet to insert at the
    /// caret so the admin never has to construct the image URL by hand.
    /// </summary>
    public async Task<IActionResult> OnPostUploadImageAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new JsonResult(new { ok = false, error = "Save the tool before uploading images." });
        }

        var file = Request.Form.Files.GetFile("imageFile");
        if (file is not { Length: > 0 })
        {
            return new JsonResult(new { ok = false, error = "No file received." });
        }

        if (!AllowedImageTypes.Contains(file.ContentType))
        {
            return new JsonResult(new { ok = false, error = "Image must be PNG, JPEG, WebP, or GIF." });
        }

        if (file.Length > MaxImageBytes)
        {
            return new JsonResult(new { ok = false, error = "Image must be at most 1 MiB." });
        }

        try
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);
            var altText = Request.Form["altText"].ToString();

            var imageId = await contentProvider.UploadToolImageAsync(
                id,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                string.IsNullOrWhiteSpace(altText) ? null : altText,
                HttpContext.RequestAborted);

            var url = $"/tools/image/{imageId}";
            var alt = string.IsNullOrWhiteSpace(altText) ? Path.GetFileNameWithoutExtension(file.FileName) : altText;
            return new JsonResult(new
            {
                ok = true,
                id = imageId.ToString(),
                url,
                fileName = file.FileName,
                markdown = $"![{alt}]({url})"
            });
        }
        catch (ContentException ex)
        {
            logger.LogWarning(ex, "Tool image upload rejected. Tool={Id} Code={Code}", id, ex.ErrorCode);
            return new JsonResult(new { ok = false, error = DescribeError(ex.ErrorCode) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload tool image. Tool={Id}", id);
            return new JsonResult(new { ok = false, error = "Upload failed." });
        }
    }

    public async Task<IActionResult> OnPostDeleteImageAsync(string id, Guid imageId)
    {
        try
        {
            await contentProvider.DeleteToolImageAsync(imageId, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = "Image deleted.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tool image. Image={ImageId}", imageId);
            TempData["ErrorMessage"] = "Failed to delete the image.";
        }

        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> RedisplayAsync()
    {
        await LoadCategoriesAsync();
        if (!IsNew && !string.IsNullOrWhiteSpace(Form.Id))
        {
            await LoadImagesAsync(Form.Id);
        }

        return Page();
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            Categories = await contentProvider.ListToolCategoriesAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tool categories.");
            ErrorMessage ??= "Failed to load categories.";
        }
    }

    private async Task LoadImagesAsync(string toolId)
    {
        try
        {
            Images = await contentProvider.ListToolImagesAsync(toolId, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tool images. Tool={Id}", toolId);
        }
    }

    private static IReadOnlyList<string> ParseTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Maps a ContentService error code to an admin-facing message.</summary>
    private static string DescribeError(string errorCode) => errorCode switch
    {
        "invalid_id" => "ID must be lowercase alphanumeric with dashes, dots or slashes (max 50 chars).",
        "invalid_display_name" => "Display name is required and must be at most 200 characters.",
        "invalid_url" => "Website must be an absolute http(s) URL.",
        "text_too_long" => "One of the text fields exceeds its length limit.",
        "unknown_category" => "The selected category no longer exists.",
        "logo_too_large" => "Logo must be at most 256 KiB.",
        "logo_type_not_allowed" => "Logo must be a valid PNG, JPEG, WebP, or SVG file.",
        "image_too_large" => "Image must be at most 1 MiB.",
        "image_type_not_allowed" => "Image must be a valid PNG, JPEG, WebP, or GIF file.",
        "image_limit_reached" => "This tool already has the maximum number of images.",
        "tool_not_found" => "Tool not found.",
        "image_not_found" => "Image not found.",
        _ => $"Operation failed ({errorCode})."
    };

    public class ToolFormInput
    {
        [Required]
        [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9._/\-]*$",
            ErrorMessage = "ID must start with a letter or digit and contain only letters, digits, dashes, underscores, dots or slashes")]
        [StringLength(50)]
        public string Id { get; set; } = "";

        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = "";

        public string? CategoryId { get; set; }

        /// <summary>Comma-separated; normalized server-side.</summary>
        public string? Tags { get; set; }

        public string? WebsiteUrl { get; set; }

        [StringLength(300)] public string? TaglineRu { get; set; }
        [StringLength(300)] public string? TaglineEn { get; set; }

        public string? DescriptionRu { get; set; }
        public string? DescriptionEn { get; set; }
        public string? SetupRu { get; set; }
        public string? SetupEn { get; set; }

        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }
    }
}
