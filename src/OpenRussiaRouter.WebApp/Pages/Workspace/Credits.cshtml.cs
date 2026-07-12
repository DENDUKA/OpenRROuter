using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;
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
    IStringLocalizer<SharedResource> localizer,
    ILogger<CreditsModel> logger) : PageModelBase
{
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;
    private readonly ILogger<CreditsModel> _logger = logger;

    /// <summary>
    /// Raw form value for the top-up amount. Bound as a string so we can parse it under
    /// <see cref="CultureInfo.InvariantCulture"/> — the regular decimal model binder
    /// would otherwise honor the request culture, which is fatal under <c>ru</c>:
    /// <c>"10.50"</c> would be parsed as <c>1050</c>.
    /// </summary>
    [BindProperty(Name = "TopUpAmount")]
    public string? TopUpAmountRaw { get; set; }

    [BindProperty]
    public string SelectedCurrency { get; set; } = "RUB";

    [BindProperty]
    public bool AcceptLegal { get; set; }

    public BalanceInfo? Balance { get; set; }
    public IReadOnlyList<TopUpInfo> TopUps { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public decimal MinTopUpUsd => paymentSettings.Value.MinTopUpUsd;
    public bool FirstTopUpBonusAvailable { get; set; }
    public decimal FirstTopUpBonusPercent { get; set; }

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
            _logger.LogWarning(ex, "Failed to load user profile for currency. UserId={UserId}", userId);
        }

        await LoadDataAsync(userId);
    }

    public async Task<IActionResult> OnPostTopUpAsync()
    {
        var userId = User.GetUserId();

        if (SelectedCurrency is not ("RUB" or "USD" or "EUR"))
        {
            await LoadDataAsync(userId);
            ErrorMessage = _localizer["Credits_InvalidCurrency"];
            return Page();
        }

        if (!AcceptLegal)
        {
            await LoadDataAsync(userId);
            ModelState.AddModelError(nameof(AcceptLegal), _localizer["Validation_AcceptLegal"]);
            return Page();
        }

        // Parse under InvariantCulture so the user can type "10.50" regardless of the
        // current UI culture (where ',' is the decimal separator under "ru").
        if (!decimal.TryParse(TopUpAmountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var topUpAmount)
            || topUpAmount <= 0)
        {
            await LoadDataAsync(userId);
            ErrorMessage = _localizer["Credits_AmountInvalid"];
            return Page();
        }

        var clientToken = Guid.NewGuid().ToString("N");

        try
        {
            var result = await checkoutService.CreateAsync(
                userId, topUpAmount, SelectedCurrency,
                providerName: null, clientToken,
                HttpContext.RequestAborted);

            return Redirect(result.PayUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Checkout failed. UserId={UserId}", userId);
            await LoadDataAsync(userId);
            ErrorMessage = _localizer["Credits_CheckoutFailed"];
            return Page();
        }
    }

    private async Task LoadDataAsync(string userId)
    {
        try { Balance = await billingProvider.GetBalanceAsync(userId); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load balance. UserId={UserId}", userId);
            ErrorMessage = _localizer["Credits_BalanceLoadFailed"];
        }

        try { TopUps = await billingProvider.ListTopUpsAsync(userId); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load top-ups. UserId={UserId}", userId);
        }

        try
        {
            var (_, _, ftEnabled, ftPercent) = await billingProvider.GetPromotionSettingsAsync();
            var hasPaidTopUp = await billingProvider.HasUserHadPaidTopUpAsync(userId);
            FirstTopUpBonusAvailable = ftEnabled && !hasPaidTopUp;
            FirstTopUpBonusPercent = ftPercent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load promo settings for credits page. UserId={UserId}", userId);
        }
    }
}
