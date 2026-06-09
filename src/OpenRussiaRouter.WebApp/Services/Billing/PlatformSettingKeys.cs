namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>Well-known keys for the admin-editable <c>billing.platform_settings</c> store.</summary>
public static class PlatformSettingKeys
{
    /// <summary>User-facing API base URL (no trailing slash, e.g. https://api.example.com) shown in the Setup guide.</summary>
    public const string PublicApiBaseUrl = "public_api_base_url";
}
