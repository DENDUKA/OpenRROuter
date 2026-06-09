using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages;

public class DocsModel(IConfiguration configuration, BillingProvider billingProvider, ILogger<DocsModel> logger) : PageModelBase
{
    /// <summary>
    /// User-facing API base URL clients point their OpenAI-compatible SDK at. Resolution order:
    /// admin setting (<c>platform_settings</c>) → <c>PublicApiBaseUrl</c> config → current request origin.
    /// The admin setting wins so prod/VPS (where WebApp and ApiGateway are on different hosts/ports)
    /// can be configured without a redeploy; the origin fallback keeps single-host/ingress setups working.
    /// </summary>
    public string ApiBaseUrl { get; private set; } = "";

    public string V1BaseUrl => $"{ApiBaseUrl}/v1";
    public string ChatCompletionsUrl => $"{ApiBaseUrl}/v1/chat/completions";

    public async Task OnGetAsync()
    {
        string? configured = null;
        try
        {
            configured = await billingProvider.GetPlatformSettingAsync(
                PlatformSettingKeys.PublicApiBaseUrl, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load public API base URL setting; falling back to config/origin.");
        }

        if (string.IsNullOrWhiteSpace(configured))
            configured = configuration["PublicApiBaseUrl"];

        ApiBaseUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";
    }
}
