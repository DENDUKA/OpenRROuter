namespace OpenRussiaRouter.WebApp.Services.Currency;

public class CbrRateSnapshot
{
    private volatile RateData? _current;

    public bool HasRates => _current != null;

    public (decimal rubPerUsd, decimal rubPerEur) GetRates()
    {
        var d = _current ?? throw new InvalidOperationException("Exchange rates not yet initialized");
        return (d.RubPerUsd, d.RubPerEur);
    }

    public void Update(decimal rubPerUsd, decimal rubPerEur)
        => _current = new RateData(rubPerUsd, rubPerEur);

    private record RateData(decimal RubPerUsd, decimal RubPerEur);
}
