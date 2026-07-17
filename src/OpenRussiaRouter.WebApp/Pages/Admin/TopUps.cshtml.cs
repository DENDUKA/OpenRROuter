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
    public IReadOnlyList<UserInfo> AllUsers { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 0;
    public bool HasNextPage => PageNumber + 1 < TotalPages;

    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)] public string[]? UserFilter { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MinAmount { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MaxAmount { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; }

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(0, P);

        try
        {
            var usersTask = authProvider.ListUsersAsync();
            var topUpsTask = billingProvider.ListAllTopUpsAsync(
                pageNumber: PageNumber,
                pageSize: PageSize,
                userIds: UserFilter is { Length: > 0 } ? UserFilter : null,
                minAmountUsd: MinAmount,
                maxAmountUsd: MaxAmount);

            await Task.WhenAll(usersTask, topUpsTask);

            AllUsers = await usersTask;
            UsersById = AllUsers.ToDictionary(u => u.Id, StringComparer.Ordinal);

            var (items, totalCount) = await topUpsTask;
            Records = items;
            TotalCount = totalCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load admin top-ups.");
            ErrorMessage = "Failed to load top-ups.";
        }
    }

    public string GetUserDisplayName(string userId) =>
        UsersById.TryGetValue(userId, out var user) ? user.Email : userId;

    public string BuildPageUrl(int page)
    {
        var parts = new List<string>();
        if (page > 0) parts.Add($"p={page}");
        if (UserFilter is { Length: > 0 })
            foreach (var u in UserFilter) parts.Add($"userFilter={Uri.EscapeDataString(u)}");
        if (MinAmount.HasValue) parts.Add($"minAmount={MinAmount.Value}");
        if (MaxAmount.HasValue) parts.Add($"maxAmount={MaxAmount.Value}");
        return "/admin/topups" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    public string BuildFilterUrl()
    {
        return BuildPageUrl(0);
    }
}
