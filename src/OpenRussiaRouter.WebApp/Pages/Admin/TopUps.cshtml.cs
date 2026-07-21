using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TopUpsModel(BillingProvider billingProvider, AuthProvider authProvider, ILogger<TopUpsModel> logger) : PageModelBase
{
    private const int PageSize = 50;

    public IReadOnlyList<AdminTopUpInfo> Records { get; set; } = [];
    public Dictionary<string, UserInfo> UsersById { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 0;
    public bool HasNextPage => PageNumber + 1 < TotalPages;

    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)] public string? UserQuery { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MinAmount { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MaxAmount { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; }

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(0, P);

        try
        {
            var users = await authProvider.ListUsersAsync();
            UsersById = users.ToDictionary(u => u.Id, StringComparer.Ordinal);

            var matchingUserIds = MatchUsers(users, UserQuery);

            var (items, totalCount) = await billingProvider.ListAllTopUpsAsync(
                pageNumber: PageNumber,
                pageSize: PageSize,
                userIds: matchingUserIds,
                minAmountUsd: MinAmount,
                maxAmountUsd: MaxAmount);

            Records = items;
            TotalCount = totalCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load admin top-ups.");
            ErrorMessage = "Failed to load top-ups.";
        }
    }

    private static IReadOnlyList<string>? MatchUsers(IReadOnlyList<UserInfo> users, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        var term = query.Trim();
        var ids = new List<string>();
        foreach (var u in users)
        {
            if (u.Email != null && u.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(u.Id);
                continue;
            }
            if (!string.IsNullOrEmpty(u.DisplayName) && u.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(u.Id);
            }
        }
        return ids;
    }

    public string GetUserDisplayName(string userId) =>
        UsersById.TryGetValue(userId, out var user) ? user.Email : userId;

    public string BuildPageUrl(int page)
    {
        var parts = new List<string>();
        if (page > 0) parts.Add($"p={page}");
        if (!string.IsNullOrWhiteSpace(UserQuery))
            parts.Add($"userQuery={Uri.EscapeDataString(UserQuery!.Trim())}");
        if (MinAmount.HasValue) parts.Add($"minAmount={MinAmount.Value}");
        if (MaxAmount.HasValue) parts.Add($"maxAmount={MaxAmount.Value}");
        return "/admin/topups" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }
}
