using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Billing;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Background worker that sweeps stale <c>Created</c> payments and asks the provider for their
/// current status. Closes the gap when a webhook is missed or arrives before <c>AttachProviderRef</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency model.</b> Billing's <c>AcquireStalePayments</c> uses <c>SKIP LOCKED</c> +
/// a persistent lease (<c>lease_owner</c>/<c>lease_until_utc</c>) so multiple WebApp replicas
/// can sweep concurrently without collision. The lease is released implicitly when the state
/// transition succeeds (Billing zeroes the columns in the same tx); payments that stay
/// <c>Created</c> are released when their lease expires.
/// </para>
/// <para>
/// <b>No separate lease-release RPC.</b> Successful transition in Billing is enough — see
/// implementation plan §Hardening (Reconciliation worker).
/// </para>
/// </remarks>
public sealed class PaymentReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly PaymentSettings _settings;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PaymentReconciliationWorker> _logger;

    public PaymentReconciliationWorker(
        IServiceProvider services,
        IOptions<PaymentSettings> options,
        IHostEnvironment environment,
        ILogger<PaymentReconciliationWorker> logger)
    {
        _services = services;
        _settings = options.Value;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The worker is meaningful only when at least one provider is registered. In a fresh dev
        // setup with no provider configured we just sleep — PaymentStartupGuard would already
        // have failed in Production, so this is a defensive no-op for Development.
        var interval = _settings.Reconciliation.Interval;
        if (interval < TimeSpan.FromSeconds(10))
        {
            interval = TimeSpan.FromSeconds(10);
        }

        // Stagger startup slightly to avoid a thundering herd right after process boot.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Reconciliation sweep failed: {Message}. Will retry in {Interval}.",
                    ex.Message, interval);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingProvider>();
        var registry = scope.ServiceProvider.GetService<PaymentProviderRegistry>();

        if (registry is null || registry.RegisteredNames.Count == 0)
        {
            // No providers configured — nothing to reconcile. Common in dev when payments are off.
            return;
        }

        var workerId = $"webapp-{Environment.MachineName}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var olderThan = DateTimeOffset.UtcNow.AddMinutes(-_settings.Reconciliation.OlderThanMinutes);

        IReadOnlyList<PaymentInfo> batch;
        try
        {
            batch = await billing.AcquireStalePaymentsAsync(
                workerId,
                _settings.Reconciliation.LeaseSeconds,
                olderThan,
                _settings.Reconciliation.BatchLimit,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "AcquireStalePayments failed (worker={Worker}): {Message}",
                workerId, ex.Message);
            return;
        }

        if (batch.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Reconciliation sweep: worker={Worker} acquired {Count} stale payment(s).",
            workerId, batch.Count);

        foreach (var payment in batch)
        {
            try
            {
                await ReconcileOneAsync(scope.ServiceProvider, billing, payment, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reconciliation failed for payment {PaymentId}: {Message}. Remaining batch will continue.",
                    payment.Id, ex.Message);
            }
        }
    }

    private async Task ReconcileOneAsync(
        IServiceProvider scope,
        BillingProvider billing,
        PaymentInfo payment,
        CancellationToken ct)
    {
        var registry = scope.GetRequiredService<PaymentProviderRegistry>();
        var processor = scope.GetRequiredService<PaymentWebhookProcessor>();
        var loggerFactory = scope.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<PaymentReconciliationWorker>();

        // Find the provider implementation for this payment. If the provider is no longer
        // registered (config change), we can't reconcile — log and skip. The lease will expire
        // and another worker (or this one, after a config reload) will pick it up.
        var provider = registry.TryGet(payment.Provider);
        if (provider is null)
        {
            logger.LogWarning(
                "Reconciliation: payment {PaymentId} references unknown provider '{Provider}'. Skipping.",
                payment.Id, payment.Provider);
            return;
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
        {
            // No provider-side id yet — AttachProviderRef never ran. The provider has no record
            // of this payment, so polling it is pointless. The lease will expire; if the user
            // eventually completes checkout, the webhook will carry the order_id and Billing will
            // resolve it. Otherwise the payment will eventually expire.
            logger.LogDebug(
                "Reconciliation: payment {PaymentId} has no provider_payment_id yet. Skipping (lease will expire).",
                payment.Id);
            return;
        }

        ProviderPaymentStatus status;
        try
        {
            status = await provider.GetPaymentAsync(payment.ProviderPaymentId!, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Reconciliation: provider {Provider} GetPaymentAsync failed for {ProviderRef}: {Message}. Will retry next sweep.",
                provider.Name, payment.ProviderPaymentId, ex.Message);
            return;
        }

        // Map the provider-side status to a webhook-style event and feed it through the normal
        // processor. This guarantees reconciliation and webhook paths converge on the same code.
        PaymentWebhookEvent? evt = MapStatusToEvent(payment, status);
        if (evt is null)
        {
            // Still Created/Pending — nothing to do, the lease will expire and we'll re-check next sweep.
            return;
        }

        // We don't have a real raw payload for reconciliation-driven events — pass an empty string.
        // Billing stores it in payment_events.raw_payload for audit; reconciliation events are
        // distinguishable by provider_event_id="reconcile:{payment_id}".
        var result = await processor.ProcessAsync(
            providerName: provider.Name,
            webhookEvent: evt,
            rawPayload: string.Empty,
            signatureOk: true,
            ct);

        logger.LogInformation(
            "Reconciliation: payment {PaymentId} ({Provider}/{ProviderRef}) → event={Event} outcome={Outcome} errorCode={ErrorCode}.",
            payment.Id, provider.Name, payment.ProviderPaymentId, evt.EventType, result.Outcome, result.ErrorCode);
    }

    private static PaymentWebhookEvent? MapStatusToEvent(PaymentInfo payment, ProviderPaymentStatus status)
    {
        // The provider-side status maps to webhook event types. We only emit events that advance
        // the state machine — Created/Pending are not actionable.
        string? eventType = status.Status switch
        {
            ProviderPaymentStatusType.Paid => PaymentWebhookEventTypes.Paid,
            ProviderPaymentStatusType.Canceled => PaymentWebhookEventTypes.Canceled,
            ProviderPaymentStatusType.Expired => PaymentWebhookEventTypes.Expired,
            ProviderPaymentStatusType.Refunded => PaymentWebhookEventTypes.Refunded,
            ProviderPaymentStatusType.ChargedBack => PaymentWebhookEventTypes.Chargeback,
            _ => null,
        };

        if (eventType is null)
        {
            return null;
        }

        // For reconciliation we synthesise a stable provider_event_id so Billing's dedup
        // (UNIQUE on (provider, provider_event_id)) makes repeated sweeps idempotent.
        var providerEventId = $"reconcile:{payment.Id}:{eventType}";

        return new PaymentWebhookEvent(
            ProviderPaymentId: payment.ProviderPaymentId,
            OrderId: payment.OrderId,
            EventType: eventType,
            ConfirmedAmount: status.ConfirmedAmount ?? payment.ProviderAmount,
            ConfirmedCurrency: status.ConfirmedCurrency ?? payment.ProviderCurrency,
            ProviderEventId: providerEventId,
            Reason: status.Reason);
    }
}
