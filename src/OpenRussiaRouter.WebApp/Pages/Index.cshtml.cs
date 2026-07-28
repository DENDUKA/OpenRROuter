using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.Contracts.Auth;
using System.Threading.Tasks;

namespace OpenRussiaRouter.WebApp.Pages;

public class IndexModel : PageModelBase 
{
    private readonly BillingProvider _billingProvider;
    private readonly AuthProvider _authProvider;
    private readonly ILogger<IndexModel> _logger;

    public bool HasBalance { get; set; }
    public bool HasApiKey { get; set; }
    public IReadOnlyList<ProviderInfo> Providers { get; private set; } = [];

    public bool StartBalanceEnabled { get; set; }
    public decimal StartBalanceAmountUsd { get; set; }
    public bool FirstTopUpEnabled { get; set; }
    public decimal FirstTopUpPercent { get; set; }
    public bool HasPaidTopUp { get; set; }
    public bool FirstTopUpBonusAvailable { get; set; }

    public IndexModel(BillingProvider billingProvider, AuthProvider authProvider, ILogger<IndexModel> logger)
    {
        _billingProvider = billingProvider;
        _authProvider = authProvider;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;

        // Fire all tasks eagerly to maximize parallelism
        var providersTask = _billingProvider.ListModelProvidersAsync(ct);
        var promoTask = _billingProvider.GetPromotionSettingsAsync(ct);

        // Await each with its own error handling
        try { Providers = await providersTask; }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load providers for landing page"); }

        try { (StartBalanceEnabled, StartBalanceAmountUsd, FirstTopUpEnabled, FirstTopUpPercent) = await promoTask; }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load promotion settings for landing page"); }

        var user = CurrentUser;
        if (user != null)
        {
            // Fire user-specific tasks eagerly
            var balanceTask = _billingProvider.GetBalanceAsync(user.UserId);
            var keysTask = _authProvider.ListApiKeysAsync(user.UserId);
            var hasPaidTopUpTask = _billingProvider.HasUserHadPaidTopUpAsync(user.UserId, ct);

            try
            {
                var balance = await balanceTask;
                HasBalance = balance.Amount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load balance for user {UserId} on landing page", user.UserId);
            }

            try
            {
                var keys = await keysTask;
                HasApiKey = keys.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load API keys for user {UserId} on landing page", user.UserId);
            }

            try
            {
                HasPaidTopUp = await hasPaidTopUpTask;
                FirstTopUpBonusAvailable = FirstTopUpEnabled && !HasPaidTopUp;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check paid top-up status for user {UserId} on landing page", user.UserId);
            }
        }
    }
}
