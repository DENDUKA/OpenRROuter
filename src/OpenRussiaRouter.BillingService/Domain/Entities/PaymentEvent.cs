namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record PaymentEvent(
    string Id,
    string PaymentId,
    string Provider,
    string? ProviderEventId,
    string? ProviderPaymentId,
    string EventType,
    string? OrderId,
    decimal? ConfirmedAmount,
    string? ConfirmedCurrency,
    string? Reason,
    string? RawPayload,
    bool SignatureOk,
    bool Processed,
    int Attempts,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc);

public sealed record PaymentEventInput(
    string Provider,
    string? ProviderPaymentId,
    string? OrderId,
    string EventType,
    decimal? ConfirmedAmount,
    string? ConfirmedCurrency,
    string? Reason,
    string? ProviderEventId,
    string? RawPayload,
    bool SignatureOk);

public sealed record ConfirmOutcome(
    bool Confirmed,
    Payment? Payment,
    string? LedgerEntryId,
    decimal BalanceUsd,
    string? ErrorCode);

public sealed record ApplyOutcome(
    bool Applied,
    Payment? Payment,
    string? LedgerEntryId,
    string? ErrorCode);
