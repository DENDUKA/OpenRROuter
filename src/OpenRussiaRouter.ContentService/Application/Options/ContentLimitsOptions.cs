using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ContentService.Application.Options;

public sealed class ContentLimitsOptions
{
    /// <summary>Logo cap, mirrors the provider logo limit and the DB check constraint.</summary>
    [Range(1024, 1024 * 1024)]
    public int MaxLogoBytes { get; set; } = 256 * 1024;

    /// <summary>Body image cap, mirrors the <c>ck_tool_images_size</c> constraint.</summary>
    [Range(1024, 8 * 1024 * 1024)]
    public int MaxImageBytes { get; set; } = 1024 * 1024;

    [Range(1, 500)]
    public int MaxImagesPerTool { get; set; } = 30;

    [Range(1024, 1024 * 1024)]
    public int MaxMarkdownChars { get; set; } = 64 * 1024;

    [Range(20, 2000)]
    public int MaxTaglineChars { get; set; } = 300;

    [Range(10, 500)]
    public int MaxDisplayNameChars { get; set; } = 200;

    [Range(10, 200)]
    public int MaxIdChars { get; set; } = 50;
}
