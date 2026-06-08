namespace OpenRussiaRouter.WebApp.Services.Currency;

public class ExchangeRateSettings
{
    public const string SectionName = "ExchangeRates";

    public string Source { get; set; } = "cbr";
    public int RefreshHourMsk { get; set; } = 12;
    public decimal MarkupPercent { get; set; } = 0;
    public FallbackRates Fallback { get; set; } = new();

    public class FallbackRates
    {
        public decimal RubPerUsd { get; set; } = 95.0m;
        public decimal RubPerEur { get; set; } = 103.0m;
    }
}
