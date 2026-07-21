using OpenRussiaRouter.BillingService.Domain.Enums;

namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class PaymentEntity
{
    public string Id { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Provider { get; init; } = "";
    public string? ProviderPaymentId { get; init; }
    public string OrderId { get; init; } = "";
    public string ClientToken { get; init; } = "";
    public decimal DisplayAmount { get; init; }
    public string DisplayCurrency { get; init; } = "";
    public decimal ProviderAmount { get; init; }
    public string ProviderCurrency { get; init; } = "";
    public decimal CreditedUsdAmount { get; init; }
    public decimal FxRate { get; init; }
    public string FxSource { get; init; } = "";
    public DateTimeOffset FxAtUtc { get; init; }
    public PaymentStatus Status { get; init; }
    public string? CancelReason { get; init; }
    public string? PayUrl { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseUntilUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? ConfirmedAtUtc { get; init; }
    public DateTimeOffset? CanceledAtUtc { get; init; }
    public decimal? BonusPercent { get; init; }
    public decimal? BonusUsd { get; init; }
}
