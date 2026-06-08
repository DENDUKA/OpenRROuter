using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Currency;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Pages.Workspace;

[Authorize]
public class CreditsModel(
    BillingProvider billingProvider,
    AuthProvider authProvider,
    CurrencyFormatter currencyFormatter,
    IOptions<PaymentSettings> paymentSettings,
    PaymentCheckoutService checkoutService,
    ILogger<CreditsModel> logger) : PageModelBase
{
    [BindProperty]
    public decimal TopUpAmount { get; set; }

    [BindProperty]
    public string SelectedCurrency { get; set; } = "RUB";

    public BalanceInfo? Balance { get; set; }
    public IReadOnlyList<TopUpInfo> TopUps { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public decimal MinTopUpUsd => paymentSettings.Value.MinTopUpUsd;

    public string FormatAmount(decimal usdAmount)
    {
        var dc = SelectedCurrency switch
        {
            "USD" => DisplayCurrency.Usd,
            "EUR" => DisplayCurrency.Eur,
            _ => DisplayCurrency.Rub
        };
        return currencyFormatter.Format(usdAmount, dc);
    }

    public async Task OnGetAsync()
    {
        var userId = User.GetUserId();
        try
        {
            var user = await authProvider.GetUserAsync(userId);
            if (user != null) SelectedCurrency = user.DisplayCurrency;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load user profile for currency. UserId={UserId}", userId);
        }

        await LoadDataAsync(userId);
    }

    public async Task<IActionResult> OnPostTopUpAsync()
    {
        var userId = User.GetUserId();

        if (SelectedCurrency is not ("RUB" or "USD" or "EUR"))
        {
            await LoadDataAsync(userId);
            ErrorMessage = "Invalid currency selection.";
            return Page();
        }

        if (TopUpAmount <= 0)
        {
            await LoadDataAsync(userId);
            ErrorMessage = "Amount must be greater than 0.";
            return Page();
        }

        var clientToken = Guid.NewGuid().ToString("N");

        try
        {
            var result = await checkoutService.CreateAsync(
                userId, TopUpAmount, SelectedCurrency,
                providerName: null, clientToken,
                HttpContext.RequestAborted);

            return Redirect(result.PayUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Checkout failed. UserId={UserId}", userId);
            await LoadDataAsync(userId);
            ErrorMessage = "Failed to initiate checkout. Please try again.";
            return Page();
        }
    }

    private async Task LoadDataAsync(string userId)
    {
        try { Balance = await billingProvider.GetBalanceAsync(userId); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load balance. UserId={UserId}", userId);
            ErrorMessage = "Failed to load balance.";
        }

        try { TopUps = await billingProvider.ListTopUpsAsync(userId); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load top-ups. UserId={UserId}", userId);
        }
    }
}
