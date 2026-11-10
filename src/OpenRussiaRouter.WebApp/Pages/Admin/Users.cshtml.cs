using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Email;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel(
    AuthProvider authProvider,
    BillingProvider billingProvider,
    ControlPlaneProvider controlPlaneProvider,
    IEmailDispatchQueue emailQueue,
    ILogger<UsersModel> logger) : PageModelBase
{
    private readonly AuthProvider _authClient = authProvider;
    private readonly BillingProvider _billingClient = billingProvider;
    private readonly ControlPlaneProvider _controlPlaneClient = controlPlaneProvider;
    private readonly IEmailDispatchQueue _emailQueue = emailQueue;
    private readonly ILogger<UsersModel> _logger = logger;

    public IReadOnlyList<UserInfo> Users { get; set; } = [];
    public Dictionary<string, decimal> UserBalances { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? Action => Request.Query["action"];
    public string? TopUpUserId => Request.Query["userId"];
    public string? NewInviteLink { get; set; }

    [BindProperty] public InviteInput Invite { get; set; } = new();
    [BindProperty] public TopUpInput TopUp { get; set; } = new();

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

            // Email the link; failure here must not break invite creation — the token is
            // still shown in the UI as a fallback for the admin to share manually.
            try
            {
                await _emailQueue.EnqueueAsync(EmailTemplates.InviteEmail(Invite.Email, NewInviteLink));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue invite email. Email={Email}", Invite.Email);
            }

            await LoadUsers();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create invite. Email={Email} Role={Role}", Invite.Email, Invite.Role);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to top up balance. AdminId={AdminId} UserId={UserId} Amount={Amount}", User.GetUserId(), userId, TopUp.Amount);
            ErrorMessage = "Failed to top up balance.";
            await LoadUsers();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableUserAsync(string userId)
    {
        if (userId == User.GetUserId())
        {
            ErrorMessage = "You cannot disable your own account.";
            await LoadUsers();
            return Page();
        }

        try
        {
            await _authClient.UpdateUserAsync(userId, status: "Disabled");
            try
            {
                var keys = await _authClient.ListApiKeysAsync(userId);
                foreach (var k in keys)
                {
                    _ = _controlPlaneClient.UpdateUserIntegrationKeyStatusAsync(k.Id, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deactivate node keys for disabled user. UserId={UserId}", userId);
            }

            return RedirectToPage("Users", new { success = "User disabled successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable user. AdminId={AdminId} UserId={UserId}", User.GetUserId(), userId);
            ErrorMessage = "Failed to disable user.";
            await LoadUsers();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEnableUserAsync(string userId)
    {
        try
        {
            await _authClient.UpdateUserAsync(userId, status: "Active");
            try
            {
                var keys = await _authClient.ListApiKeysAsync(userId);
                foreach (var k in keys.Where(k => !k.DisabledAtUtc.HasValue))
                {
                    _ = _controlPlaneClient.UpdateUserIntegrationKeyStatusAsync(k.Id, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reactivate node keys for enabled user. UserId={UserId}", userId);
            }

            return RedirectToPage("Users", new { success = "User enabled successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable user. AdminId={AdminId} UserId={UserId}", User.GetUserId(), userId);
            ErrorMessage = "Failed to enable user.";
            await LoadUsers();
            return Page();
        }
    }

    private async Task LoadUsers()
    {
        try
        {
            Users = await _authClient.ListUsersAsync();

            // Limit concurrent gRPC calls to BillingService to avoid blasting it with
            // thousands of parallel requests when the user base is large.
            const int maxConcurrency = 10;
            using var throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var balanceTasks = Users.Select(async user =>
            {
                await throttle.WaitAsync();
                try
                {
                    var b = await _billingClient.GetBalanceAsync(user.Id);
                    return (user.Id, Amount: b.Amount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load balance for user. UserId={UserId}", user.Id);
                    return (user.Id, Amount: 0m);
                }
                finally
                {
                    throttle.Release();
                }
            });
            var balances = await Task.WhenAll(balanceTasks);
            foreach (var (userId, amount) in balances)
                UserBalances[userId] = amount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users list.");
            ErrorMessage = "Failed to load users.";
        }
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
