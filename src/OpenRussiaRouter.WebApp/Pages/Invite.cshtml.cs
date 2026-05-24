using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages;

public class InviteModel : PageModelBase
{
    private readonly AuthProvider _authClient;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public InviteModel(AuthProvider AuthProvider) => _authClient = AuthProvider;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            await _authClient.CompleteInviteAsync(Token, Input.Password);
            IsSuccess = true;
            return Page();
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "validation_failed" => "Invalid or expired invite token.",
                _ => "Registration failed. Please try again."
            };
        }
        catch
        {
            ErrorMessage = "An error occurred. Please try again.";
        }

        return Page();
    }

    public class InputModel
    {
        [Required, MinLength(8)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
