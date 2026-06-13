using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class ApiKeysModel(
    AuthProvider authProvider,
    BillingProvider billingProvider,
    ControlPlaneProvider controlPlaneProvider,
    ILogger<ApiKeysModel> logger) : PageModelBase
{
    private readonly AuthProvider _authClient = authProvider;
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly ControlPlaneProvider _controlPlaneClient = controlPlaneProvider;
    private readonly ILogger<ApiKeysModel> _logger = logger;

    public IReadOnlyList<ApiKeySummary> Keys { get; set; } = [];
    public IReadOnlyDictionary<string, ApiKeyLimitSummaryInfo> LimitSummaries { get; set; } =
        new Dictionary<string, ApiKeyLimitSummaryInfo>(StringComparer.Ordinal);
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? ActionKeyId => Request.Query["keyId"];

    [TempData] public string? NewKeyValue { get; set; }

    [BindProperty] public CreateKeyInput Create { get; set; } = new();
    [BindProperty] public RenameKeyInput Rename { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            Keys = await _authClient.ListApiKeysAsync(User.GetUserId(), cancellationToken: HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load API keys. UserId={UserId}", User.GetUserId());
            ErrorMessage = "Failed to load API keys.";
            return;
        }

        if (Keys.Count == 0)
            return;

        try
        {
            LimitSummaries = await _billingClient.ListApiKeyLimitSummariesAsync(
                User.GetUserId(),
                Keys.Select(key => key.Id).ToArray(),
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load API key limits. UserId={UserId}", User.GetUserId());
            ErrorMessage = ErrorMessage is null
                ? "Failed to load API key limits."
                : ErrorMessage + " Failed to load API key limits.";
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Create.Name))
        {
            ModelState.AddModelError("Create.Name", "Key name is required.");
            await OnGetAsync();
            return Page();
        }
        try
        {
            var result = await _authClient.CreateApiKeyAsync(User.GetUserId(), Create.Name);
            NewKeyValue = result.RawKey;
            // Sync the new key to router nodes within the request lifetime. A push failure must not
            // break key creation (the key already exists), so log and continue rather than throw.
            try
            {
                await _controlPlaneClient.PushUserIntegrationKeyAsync(
                    Create.Name, result.RawKey, User.GetUserId(), result.ApiKeyId,
                    cancellationToken: HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push new API key to router nodes. UserId={UserId} KeyId={KeyId}", User.GetUserId(), result.ApiKeyId);
            }
            return RedirectToPage();
        }
        catch (AuthException ex) when (ex.ErrorCode == "validation_failed")
        {
            _logger.LogWarning(ex, "Failed to create API key due to validation/limit. UserId={UserId} Name={Name}", User.GetUserId(), Create.Name);
            ErrorMessage = "Cannot create API key. Limit reached or name is invalid.";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create API key. UserId={UserId} Name={Name}", User.GetUserId(), Create.Name);
            ErrorMessage = "Failed to create API key.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameAsync(string keyId)
    {
        if (string.IsNullOrWhiteSpace(Rename.Name))
        {
            ModelState.AddModelError("Rename.Name", "New name is required.");
            await OnGetAsync();
            return Page();
        }
        try
        {
            await _authClient.RenameApiKeyAsync(User.GetUserId(), keyId, Rename.Name);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename API key. UserId={UserId} KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = "Failed to rename key.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableKeyAsync(string keyId)
    {
        try
        {
            await _authClient.DisableApiKeyAsync(User.GetUserId(), keyId);
            _ = _controlPlaneClient.UpdateUserIntegrationKeyStatusAsync(keyId, false);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable API key. UserId={UserId} KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = "Failed to disable key.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEnableKeyAsync(string keyId)
    {
        try
        {
            await _authClient.EnableApiKeyAsync(User.GetUserId(), keyId);
            _ = _controlPlaneClient.UpdateUserIntegrationKeyStatusAsync(keyId, true);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable API key. UserId={UserId} KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = "Failed to enable key.";
            await OnGetAsync();
            return Page();
        }
    }


    public async Task<IActionResult> OnPostDeleteAsync(string keyId)
    {
        try
        {
            await _authClient.DeleteApiKeyAsync(User.GetUserId(), keyId);
            // Physically remove the key from router nodes (best-effort; the key is already
            // disabled + soft-deleted on our side, so a node failure must not block the user).
            _ = _controlPlaneClient.DeleteUserIntegrationKeyAsync(keyId);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete API key. UserId={UserId} KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = "Failed to delete key.";
            await OnGetAsync();
            return Page();
        }
    }

    public class CreateKeyInput
    {
        public string Name { get; set; } = "";
    }

    public class RenameKeyInput
    {
        public string Name { get; set; } = "";
    }

    public string FormatDailyLimit(ApiKeySummary key) => FormatLimit(key.Id, daily: true);

    public string FormatWeeklyLimit(ApiKeySummary key) => FormatLimit(key.Id, daily: false);

    public bool HasDailyLimit(ApiKeySummary key) =>
        LimitSummaries.TryGetValue(key.Id, out var summary) && summary.DailyLimitUsd.HasValue;

    public bool HasWeeklyLimit(ApiKeySummary key) =>
        LimitSummaries.TryGetValue(key.Id, out var summary) && summary.WeeklyLimitUsd.HasValue;

    public bool IsDailyLimitReached(ApiKeySummary key) =>
        LimitSummaries.TryGetValue(key.Id, out var summary) && summary.DailyExceeded;

    public bool IsWeeklyLimitReached(ApiKeySummary key) =>
        LimitSummaries.TryGetValue(key.Id, out var summary) && summary.WeeklyExceeded;

    public int DailyRemainingPercent(ApiKeySummary key) => RemainingPercent(key.Id, daily: true);

    public int WeeklyRemainingPercent(ApiKeySummary key) => RemainingPercent(key.Id, daily: false);

    private string FormatLimit(string apiKeyId, bool daily)
    {
        if (!LimitSummaries.TryGetValue(apiKeyId, out var summary))
            return "No limit";

        var limit = daily ? summary.DailyLimitUsd : summary.WeeklyLimitUsd;
        if (!limit.HasValue)
            return "No limit";

        var spent = daily ? summary.DailySpentUsd : summary.WeeklySpentUsd;
        var remaining = daily ? summary.DailyRemainingUsd : summary.WeeklyRemainingUsd;
        return $"Spent {FormatUsd(spent)} / {FormatUsd(limit.Value)}; Left {FormatUsd(remaining)}";
    }

    private int RemainingPercent(string apiKeyId, bool daily)
    {
        if (!LimitSummaries.TryGetValue(apiKeyId, out var summary))
            return 0;

        var limit = daily ? summary.DailyLimitUsd : summary.WeeklyLimitUsd;
        if (!limit.HasValue || limit.Value <= 0)
            return 0;

        var remaining = daily ? summary.DailyRemainingUsd : summary.WeeklyRemainingUsd;
        var percent = decimal.Round(Math.Clamp(remaining / limit.Value * 100m, 0m, 100m), 0, MidpointRounding.AwayFromZero);
        return (int)percent;
    }

    private static string FormatUsd(decimal amount) => "$" + amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
