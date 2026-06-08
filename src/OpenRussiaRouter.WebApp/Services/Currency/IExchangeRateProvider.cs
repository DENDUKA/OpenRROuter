namespace OpenRussiaRouter.WebApp.Services.Currency;

public interface IExchangeRateProvider
{
    decimal UsdTo(DisplayCurrency currency);
    decimal ToUsd(decimal amount, DisplayCurrency currency);
}
