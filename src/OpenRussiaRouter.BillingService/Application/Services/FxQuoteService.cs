using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BuildingBlocks.Errors;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class ExchangeRateOptions
{
    public string Source { get; set; } = "cbr";
    public decimal MarkupPercent { get; set; }
    public ExchangeRateFallbackOptions Fallback { get; set; } = new();
}

public sealed class ExchangeRateFallbackOptions
{
    public decimal RubPerUsd { get; set; } = 95m;
    public decimal RubPerEur { get; set; } = 103m;
}

public sealed class PaymentOptions
{
    public decimal MinTopUpUsd { get; set; } = 1m;
    public decimal MaxTopUpUsd { get; set; } = 10000m;
    public bool AutoReverseChargeback { get; set; }
}

public sealed record FxQuote(decimal Rate, decimal UsdPerUnit, string Source, DateTimeOffset FxAtUtc)
{
    public string QuoteId => $"{Source}:{FxAtUtc:O}";
    public DateTimeOffset ExpiresAt => FxAtUtc.AddHours(48);
}

public sealed class FxQuoteException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class FxQuoteService(
    IHttpClientFactory httpClientFactory,
    IOptions<ExchangeRateOptions> options,
    ILogger<FxQuoteService> logger)
{
    private readonly object sync = new();
    private FxSnapshot? snapshot;

    public FxQuote GetQuote(string currency, DateTimeOffset nowUtc)
    {
        var code = currency.Trim().ToUpperInvariant();
        if (code is not ("RUB" or "USD" or "EUR"))
            throw new FxQuoteException(ErrorCodes.UnsupportedCurrency);

        var current = GetOrCreateFallback(nowUtc);
        if (nowUtc - current.AtUtc > TimeSpan.FromHours(48))
            throw new FxQuoteException(ErrorCodes.FxQuoteUnavailable);

        var usdPerUnit = code switch
        {
            "USD" => 1m,
            "RUB" => 1m / current.RubPerUsd,
            "EUR" => current.RubPerEur / current.RubPerUsd,
            _ => throw new FxQuoteException(ErrorCodes.UnsupportedCurrency)
        };
        var markupFactor = 1m - (options.Value.MarkupPercent / 100m);
        usdPerUnit *= markupFactor;
        return new FxQuote(1m / usdPerUnit, usdPerUnit, current.Source, current.AtUtc);
    }

    public decimal CalculateCreditedUsd(decimal amount, string currency, DateTimeOffset nowUtc)
    {
        var quote = GetQuote(currency, nowUtc);
        return decimal.Round(amount * quote.UsdPerUnit, 6, MidpointRounding.AwayFromZero);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClientFactory.CreateClient("BillingFx")
                .GetAsync("https://www.cbr.ru/scripts/XML_daily.asp", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var rubPerUsd = ReadRate(xml, "R01235");
            var rubPerEur = ReadRate(xml, "R01239");
            lock (sync)
                snapshot = new FxSnapshot(rubPerUsd, rubPerEur, "cbr", DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh Billing FX rates; keeping last-known-good or fallback rates.");
        }
    }

    private FxSnapshot GetOrCreateFallback(DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            return snapshot ??= new FxSnapshot(
                options.Value.Fallback.RubPerUsd,
                options.Value.Fallback.RubPerEur,
                "fallback",
                nowUtc);
        }
    }

    private static decimal ReadRate(XDocument xml, string id)
    {
        var node = xml.Descendants("Valute").Single(x => (string?)x.Attribute("ID") == id);
        var nominal = decimal.Parse(node.Element("Nominal")!.Value, CultureInfo.GetCultureInfo("ru-RU"));
        var value = decimal.Parse(node.Element("Value")!.Value, CultureInfo.GetCultureInfo("ru-RU"));
        return value / nominal;
    }

    private sealed record FxSnapshot(decimal RubPerUsd, decimal RubPerEur, string Source, DateTimeOffset AtUtc);
}
