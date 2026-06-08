namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class PaymentEventEntity
{
    public string Id { get; init; } = "";
    public string PaymentId { get; init; } = "";
    public string Provider { get; init; } = "";
    public string? ProviderEventId { get; init; }
    public string? ProviderPaymentId { get; init; }
    public string EventType { get; init; } = "";
    public string? OrderId { get; init; }
    public decimal? ConfirmedAmount { get; init; }
    public string? ConfirmedCurrency { get; init; }
    public string? Reason { get; init; }
    public string? RawPayload { get; init; }
    public bool SignatureOk { get; init; }
    public bool Processed { get; init; }
    public int Attempts { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public DateTimeOffset? ProcessedAtUtc { get; init; }
}
