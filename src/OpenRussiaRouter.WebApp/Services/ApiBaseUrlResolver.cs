using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Services;

/// <summary>
/// Resolves the user-facing API base URL (no trailing slash, without <c>/v1</c>). Resolution order:
/// admin setting (<c>platform_settings</c>) → <c>PublicApiBaseUrl</c> config → current request origin.
/// The admin setting wins so prod/VPS (where WebApp and ApiGateway are on different hosts/ports) can be
/// configured without a redeploy; the origin fallback keeps single-host/ingress setups working.
/// </summary>
public sealed class ApiBaseUrlResolver(
    IConfiguration configuration,
    BillingProvider billingProvider,
    ILogger<ApiBaseUrlResolver> logger)
{
    public async Task<string> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        string? configured = null;
        try
        {
            configured = await billingProvider.GetPlatformSettingAsync(PlatformSettingKeys.PublicApiBaseUrl, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load public API base URL setting; falling back to config/origin.");
        }

        if (string.IsNullOrWhiteSpace(configured))
            configured = configuration["PublicApiBaseUrl"];

        return !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}";
    }
}
