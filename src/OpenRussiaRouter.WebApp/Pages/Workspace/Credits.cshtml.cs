using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp;
using OpenRussiaRouter.WebApp.Services.Analytics;
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
    public bool IsAdmin => User.IsInRole("Admin");
    public bool YandexPayEnabled => paymentSettings.Value.YandexPay.Enabled;
    public bool RobokassaEnabled => paymentSettings.Value.Robokassa is
    {
        MerchantLogin.Length: > 0, Password1.Length: > 0, NotifyUrl.Length: > 0
    };

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

    public async Task<IActionResult> OnGetAsync()
    {
        // Return leg from the payment provider. Turn the query flag into a one-shot goal and
        // bounce to the clean URL: with the flag gone from the address bar and TempData consumed
        // on read, refreshing the page can no longer re-report the conversion.
        if (string.Equals(Request.Query["topup"], "success", StringComparison.OrdinalIgnoreCase))
        {
            var pending = AnalyticsTempData.TakePendingTopUp(TempData);
            AnalyticsTempData.Enqueue(TempData, pending is { } paid
                ? AnalyticsGoal.Valued(AnalyticsGoals.TopUpCompleted, paid.Amount, paid.Currency)
                // The cookie did not make it back (cleared cookies, another browser, the
                // admin-only Yandex Pay checkout that never sets it). The payment still happened,
                // so count it — losing the value understates revenue, dropping the goal loses it.
                : AnalyticsGoal.Named(AnalyticsGoals.TopUpCompleted));
            return RedirectToPage();
        }

        TrackGoals(AnalyticsTempData.Dequeue(TempData));

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
        return Page();
    }

    public Task<IActionResult> OnPostTopUpAsync() => TopUpAsync(providerName: null);

    /// <summary>
    /// Robokassa checkout for admins only while the provider is being proven in production. The
    /// public flow keeps using the active provider; the button is hidden for everyone else and this
    /// handler refuses them too, since a hidden button is not an access check.
    /// </summary>
    public Task<IActionResult> OnPostTopUpRobokassaAsync() =>
        IsAdmin && RobokassaEnabled
            ? TopUpAsync(providerName: "robokassa")
            : Task.FromResult<IActionResult>(Forbid());

    private async Task<IActionResult> TopUpAsync(string? providerName)
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
                providerName, clientToken,
                HttpContext.RequestAborted);

            // Carry the amount the provider will actually charge (not what was typed) over to the
            // return leg, where it becomes the goal value.
            AnalyticsTempData.SetPendingTopUp(TempData, result.ProviderAmount, result.ProviderCurrency);

            return Redirect(result.PayUrl);
        }
        catch (BillingException ex)
        {
            // Billing owns the authoritative limits and the FX quote, so it is the only place that
            // knows WHY a checkout was refused. Surface that reason instead of a blanket "try again"
            // the user can only respond to by trying the same amount again.
            _logger.LogWarning(ex,
                "Checkout rejected by billing: {ErrorCode}. UserId={UserId} amount={Amount} {Currency}",
                ex.ErrorCode, userId, topUpAmount, SelectedCurrency);
            await LoadDataAsync(userId);
            ErrorMessage = ex.ErrorCode switch
            {
                // The limit is enforced in USD, so quote it in the user's own currency — "$1.00"
                // means nothing to someone typing rubles. Only the minimum is shown: it is the one
                // people actually hit, and naming a $10000 ceiling next to it just adds noise.
                ErrorCodes.PaymentAmountOutOfRange =>
                    _localizer["Credits_AmountOutOfRange", FormatAmount(MinTopUpUsd)],
                ErrorCodes.UnsupportedCurrency => _localizer["Credits_InvalidCurrency"],
                ErrorCodes.FxQuoteUnavailable => _localizer["Credits_FxUnavailable"],
                _ => _localizer["Credits_CheckoutFailed"],
            };
            return Page();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Provider-side failures land here. Stay provider-agnostic: this page must not know
            // which aggregator is active.
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
