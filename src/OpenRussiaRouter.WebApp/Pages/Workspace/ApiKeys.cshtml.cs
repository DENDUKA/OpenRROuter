using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class ApiKeysModel : PageModelBase
{
    private readonly AuthClient _authClient;

    public IReadOnlyList<ApiKeyInfo> Keys { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? ActionKeyId => Request.Query["keyId"];

    [TempData] public string? NewKeyValue { get; set; }

    [BindProperty] public CreateKeyInput Create { get; set; } = new();
    [BindProperty] public RenameKeyInput Rename { get; set; } = new();

    public ApiKeysModel(AuthClient authClient) => _authClient = authClient;

    public async Task OnGetAsync()
    {
        try { Keys = await _authClient.ListApiKeysAsync(User.GetUserId()); }
        catch { ErrorMessage = "Failed to load API keys."; }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        // Clear validation errors from the Rename form (not submitted in this handler)
        ModelState.Remove("Rename.Name");
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        try
        {
            var result = await _authClient.CreateApiKeyAsync(User.GetUserId(), Create.Name);
            NewKeyValue = result.RawKey;
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to create API key.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameAsync(string keyId)
    {
        // Clear validation errors from the Create form (not submitted in this handler)
        ModelState.Remove("Create.Name");
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        try
        {
            await _authClient.RenameApiKeyAsync(User.GetUserId(), keyId, Rename.Name);
            return RedirectToPage();
        }
        catch
        {
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
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to revoke key.";
            await OnGetAsync();
            return Page();
        }
    }

    public class CreateKeyInput
    {
        [Required, MinLength(1)]
        public string Name { get; set; } = "";
    }

    public class RenameKeyInput
    {
        [Required, MinLength(1)]
        public string Name { get; set; } = "";
    }
}
