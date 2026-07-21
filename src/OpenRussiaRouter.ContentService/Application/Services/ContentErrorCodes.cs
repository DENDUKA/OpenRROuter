namespace OpenRussiaRouter.ContentService.Application.Services;

/// <summary>
/// Stable machine-readable codes returned in the <c>error_code</c> field of mutating responses.
/// The WebApp maps them to localized messages; never surface raw exception text to a browser.
/// </summary>
public static class ContentErrorCodes
{
    public const string InvalidId = "invalid_id";
    public const string InvalidDisplayName = "invalid_display_name";
    public const string InvalidUrl = "invalid_url";
    public const string TextTooLong = "text_too_long";
    public const string UnknownCategory = "unknown_category";
    public const string LogoTooLarge = "logo_too_large";
    public const string LogoTypeNotAllowed = "logo_type_not_allowed";
    public const string ImageTooLarge = "image_too_large";
    public const string ImageTypeNotAllowed = "image_type_not_allowed";
    public const string ImageLimitReached = "image_limit_reached";
    public const string ToolNotFound = "tool_not_found";
    public const string ImageNotFound = "image_not_found";
    public const string CategoryNotFound = "category_not_found";
}
