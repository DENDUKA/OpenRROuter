using System.Globalization;

namespace OpenRussiaRouter.WebApp.Services.Currency;

public class CurrencyFormatter
{
    private readonly IExchangeRateProvider _rates;

    public CurrencyFormatter(IExchangeRateProvider rates) => _rates = rates;

    public string Format(decimal usdAmount, DisplayCurrency currency)
    {
        var amount = usdAmount * _rates.UsdTo(currency);
        var symbol = currency switch
        {
            DisplayCurrency.Usd => "$",
            DisplayCurrency.Rub => "₽",
            DisplayCurrency.Eur => "€",
            _ => throw new ArgumentOutOfRangeException(nameof(currency))
        };
        var text = amount.ToString("F2", CultureInfo.InvariantCulture);
        if (amount != 0 && text.TrimStart(symbol[0]) == "0.00")
            text = amount.ToString("G", CultureInfo.InvariantCulture);
        return $"{symbol}{text}";
    }
}
