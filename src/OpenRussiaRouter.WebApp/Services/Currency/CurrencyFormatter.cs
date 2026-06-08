using System.Globalization;

namespace OpenRussiaRouter.WebApp.Services.Currency;

public class CurrencyFormatter
{
    private readonly IExchangeRateProvider _rates;

    public CurrencyFormatter(IExchangeRateProvider rates) => _rates = rates;

    public string Format(decimal usdAmount, DisplayCurrency currency)
    {
        var amount = usdAmount * _rates.UsdTo(currency);
        return currency switch
        {
            DisplayCurrency.Usd => $"${amount.ToString("F2", CultureInfo.InvariantCulture)}",
            DisplayCurrency.Rub => $"₽{amount.ToString("F2", CultureInfo.InvariantCulture)}",
            DisplayCurrency.Eur => $"€{amount.ToString("F2", CultureInfo.InvariantCulture)}",
            _ => throw new ArgumentOutOfRangeException(nameof(currency))
        };
    }
}
