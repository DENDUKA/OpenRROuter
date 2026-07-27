using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Currency;

namespace OpenRussiaRouter.WebApp.Pages;

public abstract class CatalogPageModelBase : PageModelBase
{
    protected abstract AuthProvider AuthProvider { get; }
    protected abstract CurrencyFormatter CurrencyFormatter { get; }
    protected abstract ILogger Logger { get; }
    protected abstract string LogContext { get; }

    /// <summary>Display currency: profile choice for signed-in users, USD for anonymous.</summary>
    public DisplayCurrency Currency { get; protected set; } = DisplayCurrency.Usd;
    public string CurrencySymbol => Currency.Symbol();

    public string FormatPrice(decimal usdPrice) => CurrencyFormatter.Format(usdPrice, Currency);

    protected async Task<DisplayCurrency> ResolveCurrencyAsync()
    {
        var userId = CurrentUser?.UserId;
        if (string.IsNullOrEmpty(userId)) return DisplayCurrency.Usd;
        try
        {
            var user = await AuthProvider.GetUserAsync(userId, HttpContext.RequestAborted);
            return DisplayCurrencyExtensions.Parse(user?.DisplayCurrency);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to resolve display currency for {Context}. UserId={UserId}", LogContext, userId);
            return DisplayCurrency.Usd;
        }
    }
}
