namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>Well-known keys for the admin-editable <c>billing.platform_settings</c> store.</summary>
public static class PlatformSettingKeys
{
    /// <summary>User-facing API base URL (no trailing slash, e.g. https://api.example.com) shown in the Setup guide.</summary>
    public const string PublicApiBaseUrl = "public_api_base_url";

    /// <summary>Whether the start balance promotion ($1 after email verification) is enabled.</summary>
    public const string PromotionsStartBalanceEnabled = "promotions.start_balance.enabled";

    /// <summary>Amount in USD for the start balance promotion.</summary>
    public const string PromotionsStartBalanceAmountUsd = "promotions.start_balance.amount_usd";

    /// <summary>Whether the first top-up bonus promotion is enabled.</summary>
    public const string PromotionsFirstTopUpEnabled = "promotions.first_top_up.enabled";

    /// <summary>Percentage bonus for the first top-up promotion.</summary>
    public const string PromotionsFirstTopUpPercent = "promotions.first_top_up.percent";
}
