using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UserEditModel(AuthProvider authProvider, ControlPlaneProvider controlPlaneProvider) : PageModelBase
{
    public UserInfo? TargetUser { get; private set; }
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public UserEditInput Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string userId)
    {
        TargetUser = await authProvider.GetUserAsync(userId);
        if (TargetUser == null) return NotFound();

        Form.DisplayName = TargetUser.DisplayName;
        Form.MaxApiKeys = TargetUser.MaxApiKeys;
        Form.Status = TargetUser.Status;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string userId)
    {
        TargetUser = await authProvider.GetUserAsync(userId);
        if (TargetUser == null) return NotFound();

        if (userId == User.GetUserId() && Form.Status == "Disabled")
        {
            ErrorMessage = "You cannot disable your own account.";
            return Page();
        }

        if (!ModelState.IsValid) return Page();

        try
        {
            var statusChanged = TargetUser.Status != Form.Status;
            await authProvider.UpdateUserAsync(userId, Form.DisplayName, Form.MaxApiKeys, status: Form.Status);

            if (statusChanged)
            {
                try
                {
                    var keys = await authProvider.ListApiKeysAsync(userId);
                    var isActive = Form.Status == "Active";
                    foreach (var k in keys)
                    {
                        if (isActive && k.DisabledAtUtc.HasValue) continue;
                        _ = controlPlaneProvider.UpdateUserIntegrationKeyStatusAsync(k.Id, isActive);
                    }
                }
                catch
                {
                    // best-effort sync to nodes
                }
            }

            return RedirectToPage("Users", new { success = "User updated successfully." });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Update failed: {ex.Message}";
            return Page();
        }
    }
}

public class UserEditInput
{
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [Required]
    [Range(0, 1000)]
    [Display(Name = "Max API Keys")]
    public int MaxApiKeys { get; set; } = 10;

    [Required]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Active";
}