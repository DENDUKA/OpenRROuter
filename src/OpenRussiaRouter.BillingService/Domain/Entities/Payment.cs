using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record Payment(
    string Id,
    string UserId,
    string Provider,
    string? ProviderPaymentId,
    string OrderId,
    string ClientToken,
    decimal DisplayAmount,
    string DisplayCurrency,
    decimal ProviderAmount,
    string ProviderCurrency,
    MoneyAmount CreditedUsd,
    decimal FxRate,
    string FxSource,
    DateTimeOffset FxAtUtc,
    PaymentStatus Status,
    string? CancelReason,
    string? LeaseOwner,
    DateTimeOffset? LeaseUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? CanceledAtUtc,
    string? PayUrl = null,
    decimal? BonusPercent = null,
    decimal? BonusUsd = null);
