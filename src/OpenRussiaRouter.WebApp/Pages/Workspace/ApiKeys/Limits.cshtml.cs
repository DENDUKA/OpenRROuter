using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace.ApiKeys;

[Authorize]
public class LimitsModel(
    AuthProvider authProvider,
    BillingProvider billingProvider,
    IStringLocalizer<SharedResource> localizer,
    ILogger<LimitsModel> logger) : PageModelBase
{
    private readonly AuthProvider _authClient = authProvider;
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;
    private readonly ILogger<LimitsModel> _logger = logger;

    public ApiKeySummary? Key { get; set; }
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public LimitInput Form { get; set; } = new();

    public async Task OnGetAsync(string keyId)
    {
        await LoadKeyAsync(keyId, HttpContext.RequestAborted);
        if (Key is null)
            return;

        try
        {
            var settings = await _billingClient.GetApiKeyLimitSettingsAsync(
                User.GetUserId(),
                keyId,
                HttpContext.RequestAborted);

            Form = new LimitInput
            {
                ApiKeyId = keyId,
                DailyLimitUsd = FormatOptional(settings.DailyLimitUsd),
                WeeklyLimitUsd = FormatOptional(settings.WeeklyLimitUsd)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load API key limit settings. UserId={UserId}; KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = _localizer["ApiKey_LimitLoadFailed"];
            Form.ApiKeyId = keyId;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadKeyAsync(Form.ApiKeyId, HttpContext.RequestAborted);
        if (Key is null)
        {
            ErrorMessage = _localizer["ApiKey_NotFound"];
            return Page();
        }

        var daily = ParseLimit(Form.DailyLimitUsd, "Form.DailyLimitUsd", _localizer["Limits_DailyLabel"]);
        var weekly = ParseLimit(Form.WeeklyLimitUsd, "Form.WeeklyLimitUsd", _localizer["Limits_WeeklyLabel"]);
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await _billingClient.UpsertApiKeyLimitSettingsAsync(
                User.GetUserId(),
                Form.ApiKeyId,
                daily,
                weekly,
                HttpContext.RequestAborted);

            return RedirectToPage("/Workspace/ApiKeys");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save API key limits. UserId={UserId}; KeyId={KeyId}", User.GetUserId(), Form.ApiKeyId);
            ErrorMessage = _localizer["ApiKey_LimitSaveFailed"];
            return Page();
        }
    }

    private async Task LoadKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            ErrorMessage = _localizer["ApiKey_NotFound"];
            return;
        }

        try
        {
            var keys = await _authClient.ListApiKeysAsync(User.GetUserId(), cancellationToken: cancellationToken);
            Key = keys.FirstOrDefault(key => string.Equals(key.Id, keyId, StringComparison.Ordinal));
            if (Key is null)
            {
                ErrorMessage = _localizer["ApiKey_NotFound"];
                return;
            }

            Form.ApiKeyId = keyId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load API key. UserId={UserId}; KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = _localizer["ApiKey_LoadFailed"];
        }
    }

    private decimal? ParseLimit(string? value, string modelStateKey, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            ModelState.AddModelError(modelStateKey, _localizer["ApiKey_LimitPositive", displayName]);
            return null;
        }

        return parsed;
    }

    private static string? FormatOptional(decimal? value) =>
        value?.ToString("0.######", CultureInfo.InvariantCulture);

    public class LimitInput
    {
        public string ApiKeyId { get; set; } = "";
        public string? DailyLimitUsd { get; set; }
        public string? WeeklyLimitUsd { get; set; }
    }
}
