using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;

namespace OpenRussiaRouter.WebApp.Pages;

public class InviteModel : PageModelBase
{
    private readonly AuthProvider _authClient;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<InviteModel> _logger;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public InviteModel(AuthProvider AuthProvider, IStringLocalizer<SharedResource> localizer, ILogger<InviteModel> logger)
    {
        _authClient = AuthProvider;
        _localizer = localizer;
        _logger = logger;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!Input.AcceptLegal)
        {
            ModelState.AddModelError("Input.AcceptLegal", _localizer["Validation_AcceptLegal"]);
            return Page();
        }

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
                "validation_failed" => _localizer["Auth_InvalidInvite"],
                _ => _localizer["Auth_InviteFailed"]
            };
        }
        catch (Exception ex)
        {
            // Do not log the raw invite token — it is a secret credential.
            _logger.LogError(ex, "Unexpected error completing invite.");
            ErrorMessage = _localizer["Auth_InviteError"];
        }

        return Page();
    }

    public class InputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [MinLength(8, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordMin8")]
        public string Password { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_Required")]
        [Compare(nameof(Password), ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_PasswordsMismatch")]
        public string ConfirmPassword { get; set; } = "";

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Validation_AcceptLegal")]
        public bool AcceptLegal { get; set; }
    }
}
