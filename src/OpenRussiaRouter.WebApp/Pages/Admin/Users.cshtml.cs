using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel : PageModelBase
{
    private readonly AuthClient _authClient;
    private readonly BillingClient _billingClient;

    public IReadOnlyList<UserInfo> Users { get; set; } = [];
    public Dictionary<string, decimal> UserBalances { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? TopUpUserId => Request.Query["userId"];
    public string? NewInviteLink { get; set; }

    [BindProperty] public InviteInput Invite { get; set; } = new();
    [BindProperty] public TopUpInput TopUp { get; set; } = new();

    public UsersModel(AuthClient authClient, BillingClient billingClient)
    {
        _authClient = authClient;
        _billingClient = billingClient;
    }

    public async Task OnGetAsync() => await LoadUsers();

    public async Task<IActionResult> OnPostCreateInviteAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadUsers();
            return Page();
        }
        try
        {
            var result = await _authClient.CreateInviteAsync(Invite.Email, Invite.Role, User.GetUserId());
            NewInviteLink = $"{Request.Scheme}://{Request.Host}/invite/{result.RawToken}";
            await LoadUsers();
            return Page();
        }
        catch
        {
            ErrorMessage = "Failed to create invite.";
            await LoadUsers();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostTopUpAsync(string userId)
    {
        try
        {
            await _billingClient.CreateVirtualTopUpAsync(User.GetUserId(), userId, TopUp.Amount, TopUp.Reason);
            return RedirectToPage();
        }
        catch
        {
            ErrorMessage = "Failed to top up balance.";
            await LoadUsers();
            return Page();
        }
    }

    private async Task LoadUsers()
    {
        try
        {
            Users = await _authClient.ListUsersAsync();
            foreach (var user in Users)
            {
                try { var b = await _billingClient.GetBalanceAsync(user.Id); UserBalances[user.Id] = b.Amount; }
                catch { UserBalances[user.Id] = 0; }
            }
        }
        catch { ErrorMessage = "Failed to load users."; }
    }

    public class InviteInput
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Role { get; set; } = "User";
    }

    public class TopUpInput
    {
        public decimal Amount { get; set; } = 1000;
        public string Reason { get; set; } = "Virtual top-up";
    }
}
