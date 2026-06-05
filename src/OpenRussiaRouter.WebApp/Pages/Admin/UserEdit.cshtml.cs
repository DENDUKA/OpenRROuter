using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UserEditModel(AuthProvider authProvider) : PageModelBase
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

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string userId)
    {
        TargetUser = await authProvider.GetUserAsync(userId);
        if (TargetUser == null) return NotFound();

        if (!ModelState.IsValid) return Page();

        try
        {
            await authProvider.UpdateUserAsync(userId, Form.DisplayName, Form.MaxApiKeys);
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
}