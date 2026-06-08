namespace OpenRussiaRouter.WebApp.Services.Currency;

public class CbrExchangeRateProvider : IExchangeRateProvider
{
    private readonly CbrRateSnapshot _snapshot;

    public CbrExchangeRateProvider(CbrRateSnapshot snapshot) => _snapshot = snapshot;

    public decimal UsdTo(DisplayCurrency currency)
    {
        if (currency == DisplayCurrency.Usd) return 1m;
        var (rubPerUsd, rubPerEur) = _snapshot.GetRates();
        return currency switch
        {
            DisplayCurrency.Rub => rubPerUsd,
            DisplayCurrency.Eur => rubPerUsd / rubPerEur,
            _ => throw new ArgumentOutOfRangeException(nameof(currency))
        };
    }

    public decimal ToUsd(decimal amount, DisplayCurrency currency)
    {
        if (currency == DisplayCurrency.Usd) return amount;
        var (rubPerUsd, rubPerEur) = _snapshot.GetRates();
        return currency switch
        {
            DisplayCurrency.Rub => amount / rubPerUsd,
            DisplayCurrency.Eur => amount * rubPerEur / rubPerUsd,
            _ => throw new ArgumentOutOfRangeException(nameof(currency))
        };
    }
}
