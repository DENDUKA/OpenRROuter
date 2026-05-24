using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class ApiKeysModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly ControlPlaneProvider _controlPlaneClient;
    private readonly ILogger<ApiKeysModel> _logger;

    public IReadOnlyList<ApiKeyInfo> Keys { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? ActionKeyId => Request.Query["keyId"];

    [TempData] public string? NewKeyValue { get; set; }

    [BindProperty] public CreateKeyInput Create { get; set; } = new();
    [BindProperty] public RenameKeyInput Rename { get; set; } = new();

    public ApiKeysModel(AuthProvider AuthProvider, ControlPlaneProvider ControlPlaneProvider, ILogger<ApiKeysModel> logger)
    {
        _authClient = AuthProvider;
        _controlPlaneClient = ControlPlaneProvider;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        try { Keys = await _authClient.ListApiKeysAsync(User.GetUserId()); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load API keys. UserId={UserId}", User.GetUserId());
            ErrorMessage = "Failed to load API keys.";
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
            _ = _controlPlaneClient.PushUserIntegrationKeyAsync(Create.Name, result.RawKey, User.GetUserId(), result.ApiKeyId);
            return RedirectToPage();
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

    public async Task<IActionResult> OnPostRevokeAsync(string keyId)
    {
        try
        {
            await _authClient.RevokeApiKeyAsync(User.GetUserId(), keyId);
            _ = _controlPlaneClient.RevokeUserIntegrationKeyAsync(keyId);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke API key. UserId={UserId} KeyId={KeyId}", User.GetUserId(), keyId);
            ErrorMessage = "Failed to revoke key.";
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
}
