namespace OpenRussiaRouter.WebApp.Services.Currency;

public enum DisplayCurrency { Rub, Usd, Eur }

public static class DisplayCurrencyExtensions
{
    /// <summary>Parses a stored currency code (e.g. profile "USD"/"EUR"/"RUB"); unknown/null → Rub.</summary>
    public static DisplayCurrency Parse(string? code) => code switch
    {
        "USD" => DisplayCurrency.Usd,
        "EUR" => DisplayCurrency.Eur,
        _ => DisplayCurrency.Rub
    };

    public static string Symbol(this DisplayCurrency currency) => currency switch
    {
        DisplayCurrency.Usd => "$",
        DisplayCurrency.Eur => "€",
        _ => "₽"
    };
}
