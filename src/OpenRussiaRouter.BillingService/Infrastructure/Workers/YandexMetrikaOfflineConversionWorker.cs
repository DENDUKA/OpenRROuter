using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BillingService.Infrastructure.Workers;

internal sealed class YandexMetrikaOfflineConversionWorker(
    IBillingRepository repository,
    IYandexMetrikaIdentityStore identityStore,
    IYandexMetrikaClient client,
    IOptions<YandexMetrikaOfflineConversionOptions> options,
    TimeProvider timeProvider,
    ILogger<YandexMetrikaOfflineConversionWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"ym-{Guid.NewGuid():N}";
    private readonly YandexMetrikaOfflineConversionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.WorkerInterval);
        do
        {
            await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task ProcessBatchAsync(CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow();
        IReadOnlyCollection<YandexMetrikaOfflineConversion> conversions;
        try
        {
            conversions = await repository.AcquireYandexMetrikaOfflineConversionsAsync(
                _workerId,
                _options.LeaseDuration,
                nowUtc,
                _options.BatchSize,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to acquire Yandex Metrica offline conversions.");
            return;
        }

        foreach (var conversion in conversions)
        {
            await ProcessConversionAsync(conversion, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessConversionAsync(
        YandexMetrikaOfflineConversion conversion,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var clientId = conversion.ResolvedClientId;
        if (clientId is null)
        {
            if (conversion.OccurredAtUtc <= nowUtc.Subtract(_options.ExpirationWindow))
            {
                await TransitionAsync(
                    conversion,
                    YandexMetrikaOfflineConversionStatus.Expired,
                    nowUtc,
                    null,
                    null,
                    "client_id_expired",
                    nowUtc,
                    ct).ConfigureAwait(false);
                return;
            }

            try
            {
                clientId = await identityStore.GetClientIdAsync(conversion.UserId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ScheduleRetryAsync(
                    conversion,
                    YandexMetrikaOfflineConversionStatus.Pending,
                    _options.BaseRetryDelay,
                    "identity_store_unavailable",
                    nowUtc,
                    ct).ConfigureAwait(false);
                logger.LogWarning(ex, "Yandex Metrica identity lookup failed.");
                return;
            }

            if (clientId is null)
            {
                await ScheduleRetryAsync(
                    conversion,
                    YandexMetrikaOfflineConversionStatus.Pending,
                    _options.MissingIdentityRetryInterval,
                    "client_id_not_available",
                    nowUtc,
                    ct).ConfigureAwait(false);
                return;
            }

            clientId = await repository.SetYandexMetrikaClientIdAsync(
                conversion.Id,
                clientId,
                nowUtc,
                ct).ConfigureAwait(false);
            if (clientId is null)
            {
                return;
            }
        }

        try
        {
            YandexMetrikaUploading uploading;
            if (conversion.UploadId is { } uploadId)
            {
                uploading = await client.GetUploadingAsync(uploadId, ct).ConfigureAwait(false);
            }
            else if (conversion.Status == YandexMetrikaOfflineConversionStatus.Reconciling)
            {
                uploading = await client.FindByCommentAsync(conversion.Comment, ct).ConfigureAwait(false)
                    ?? await client.UploadAsync(conversion, clientId, ct).ConfigureAwait(false);
            }
            else
            {
                uploading = await client.UploadAsync(conversion, clientId, ct).ConfigureAwait(false);
            }

            await ApplyUploadingAsync(conversion, uploading, nowUtc, ct).ConfigureAwait(false);
        }
        catch (YandexMetrikaApiException ex)
        {
            await HandleApiFailureAsync(conversion, ex, nowUtc, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            await ScheduleRetryAsync(
                conversion,
                YandexMetrikaOfflineConversionStatus.Reconciling,
                RetryDelay(conversion.Attempts),
                "request_timeout",
                nowUtc,
                ct).ConfigureAwait(false);
            logger.LogWarning(ex, "Yandex Metrica request timed out.");
        }
        catch (HttpRequestException ex)
        {
            await ScheduleRetryAsync(
                conversion,
                YandexMetrikaOfflineConversionStatus.Reconciling,
                RetryDelay(conversion.Attempts),
                "network_error",
                nowUtc,
                ct).ConfigureAwait(false);
            logger.LogWarning(ex, "Yandex Metrica request failed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ScheduleRetryAsync(
                conversion,
                YandexMetrikaOfflineConversionStatus.Reconciling,
                RetryDelay(conversion.Attempts),
                "invalid_response",
                nowUtc,
                ct).ConfigureAwait(false);
            logger.LogError(ex, "Yandex Metrica returned an invalid response.");
        }
    }

    private async Task ApplyUploadingAsync(
        YandexMetrikaOfflineConversion conversion,
        YandexMetrikaUploading uploading,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (string.Equals(uploading.Status, "LINKAGE_FAILURE", StringComparison.Ordinal))
        {
            await TransitionAsync(
                conversion,
                YandexMetrikaOfflineConversionStatus.Failed,
                nowUtc,
                uploading.Id,
                uploading.Status,
                "linkage_failure",
                nowUtc,
                ct).ConfigureAwait(false);
            return;
        }

        if (string.Equals(uploading.Status, "PROCESSED", StringComparison.Ordinal))
        {
            var validRowCounts = uploading.SourceQuantity == 1 && uploading.LineQuantity == 1;
            await TransitionAsync(
                conversion,
                validRowCounts
                    ? YandexMetrikaOfflineConversionStatus.Processed
                    : YandexMetrikaOfflineConversionStatus.Failed,
                nowUtc,
                uploading.Id,
                uploading.Status,
                validRowCounts ? null : "row_count_mismatch",
                nowUtc,
                ct).ConfigureAwait(false);
            return;
        }

        var hasMismatch = uploading.SourceQuantity > 0 && uploading.SourceQuantity != 1 ||
            uploading.LineQuantity > 0 && uploading.LineQuantity != 1;
        await TransitionAsync(
            conversion,
            hasMismatch
                ? YandexMetrikaOfflineConversionStatus.Failed
                : YandexMetrikaOfflineConversionStatus.Uploaded,
            hasMismatch ? nowUtc : nowUtc.Add(_options.UploadStatusPollInterval),
            uploading.Id,
            uploading.Status,
            hasMismatch ? "row_count_mismatch" : null,
            nowUtc,
            ct).ConfigureAwait(false);
    }

    private async Task HandleApiFailureAsync(
        YandexMetrikaOfflineConversion conversion,
        YandexMetrikaApiException exception,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var statusCode = exception.StatusCode;
        if (statusCode == HttpStatusCode.BadRequest ||
            ((int)statusCode is >= 400 and < 500 &&
             statusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden &&
             statusCode != HttpStatusCode.TooManyRequests))
        {
            await TransitionAsync(
                conversion,
                YandexMetrikaOfflineConversionStatus.Failed,
                nowUtc,
                null,
                null,
                $"http_{(int)statusCode}",
                nowUtc,
                ct).ConfigureAwait(false);
            return;
        }

        var configurationError = statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        var delay = exception.RetryAfter ??
            (configurationError ? _options.ConfigurationRetryInterval : RetryDelay(conversion.Attempts));
        await ScheduleRetryAsync(
            conversion,
            conversion.UploadId.HasValue || (int)statusCode >= 500
                ? YandexMetrikaOfflineConversionStatus.Reconciling
                : YandexMetrikaOfflineConversionStatus.Pending,
            delay,
            $"http_{(int)statusCode}",
            nowUtc,
            ct).ConfigureAwait(false);

        if (configurationError)
        {
            logger.LogCritical(
                "Yandex Metrica rejected OAuth configuration with HTTP {StatusCode}.",
                (int)statusCode);
        }
    }

    private Task ScheduleRetryAsync(
        YandexMetrikaOfflineConversion conversion,
        YandexMetrikaOfflineConversionStatus status,
        TimeSpan delay,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken ct) =>
        TransitionAsync(conversion, status, nowUtc.Add(delay), null, null, error, nowUtc, ct);

    private async Task TransitionAsync(
        YandexMetrikaOfflineConversion conversion,
        YandexMetrikaOfflineConversionStatus status,
        DateTimeOffset nextAttemptAtUtc,
        long? uploadId,
        string? uploadStatus,
        string? lastError,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        await repository.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            status,
            nextAttemptAtUtc,
            uploadId,
            uploadStatus,
            lastError,
            nowUtc,
            ct).ConfigureAwait(false);
        YandexMetrikaOfflineConversionMetrics.RecordTransition(conversion.Target, status.ToString());
    }

    private TimeSpan RetryDelay(int attempts)
    {
        var multiplier = Math.Pow(2, Math.Min(attempts, 6));
        var delay = TimeSpan.FromTicks((long)(_options.BaseRetryDelay.Ticks * multiplier));
        return delay <= _options.MaxRetryDelay ? delay : _options.MaxRetryDelay;
    }
}
