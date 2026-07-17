using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TopUpsModel(BillingProvider billingProvider, AuthProvider authProvider, ILogger<TopUpsModel> logger) : PageModelBase
{
    public IReadOnlyList<AdminTopUpInfo> Records { get; set; } = [];
    public Dictionary<string, UserInfo> UsersById { get; set; } = new();
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
    public string? Cursor { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(string? cursor = null)
    {
        Cursor = cursor;
        try
        {
            var (items, hasMore, nextCursor) = await billingProvider.ListAllTopUpsAsync(afterCursor: cursor, pageSize: 50);
            Records = items;
            HasMore = hasMore;
            NextCursor = nextCursor;

            var users = await authProvider.ListUsersAsync();
            UsersById = users.ToDictionary(u => u.Id, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load admin top-ups.");
            ErrorMessage = "Failed to load top-ups.";
        }
    }

    public string GetUserDisplayName(string userId) =>
        UsersById.TryGetValue(userId, out var user) ? user.Email : userId;
}
