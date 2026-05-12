using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record LedgerEntry(
    string Id,
    string UserId,
    LedgerEntryType Type,
    MoneyAmount Amount,
    string IdempotencyKey,
    string Reason,
    DateTimeOffset CreatedAtUtc);

