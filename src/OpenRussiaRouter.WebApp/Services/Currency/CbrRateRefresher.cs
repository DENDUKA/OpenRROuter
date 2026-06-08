using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Currency;

public class CbrRateRefresher : BackgroundService
{
    private static readonly int[] RetryBackoffSeconds = [5, 15, 60];

    private readonly CbrClient _client;
    private readonly CbrRateSnapshot _snapshot;
    private readonly ExchangeRateSettings _settings;
    private readonly ILogger<CbrRateRefresher> _logger;

    public CbrRateRefresher(
        CbrClient client,
        CbrRateSnapshot snapshot,
        IOptions<ExchangeRateSettings> settings,
        ILogger<CbrRateRefresher> logger)
    {
        _client = client;
        _snapshot = snapshot;
        _settings = settings.Value;
        _logger = logger;
        // Pre-populate with fallback so rates are available before the first successful fetch.
        if (!_snapshot.HasRates)
            _snapshot.Update(_settings.Fallback.RubPerUsd, _settings.Fallback.RubPerEur);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await FetchWithRetryAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRefresh();
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
            await FetchWithRetryAsync(stoppingToken);
        }
    }

    internal async Task FetchWithRetryAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt <= RetryBackoffSeconds.Length; attempt++)
        {
            try
            {
                var (rubPerUsd, rubPerEur) = await _client.FetchRatesAsync(ct);
                _snapshot.Update(rubPerUsd, rubPerEur);
                _logger.LogInformation(
                    "CBR rates updated: 1 USD = {RubPerUsd} RUB, 1 EUR = {RubPerEur} RUB",
                    rubPerUsd, rubPerEur);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt == RetryBackoffSeconds.Length)
                {
                    _logger.LogError(ex,
                        "CBR fetch failed after {Attempts} attempts, keeping last-known-good rates",
                        attempt + 1);
                    return;
                }
                var backoff = RetryBackoffSeconds[attempt];
                _logger.LogWarning(ex,
                    "CBR fetch attempt {Attempt} failed, retrying in {Seconds}s", attempt + 1, backoff);
                try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private TimeSpan DelayUntilNextRefresh()
    {
        // MSK = UTC+3 (no DST in Russia since 2014)
        var mskNow = DateTime.UtcNow.AddHours(3);
        var mskTarget = mskNow.Date.AddHours(_settings.RefreshHourMsk);
        if (mskNow >= mskTarget)
            mskTarget = mskTarget.AddDays(1);
        return mskTarget - mskNow;
    }
}
