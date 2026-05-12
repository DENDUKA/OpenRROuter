using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Domain;

/// <summary>
/// Append-only balance ledger entry.
/// </summary>
/// <param name="Id">Ledger entry id.</param>
/// <param name="UserId">Balance owner user id.</param>
/// <param name="Type">Ledger entry type.</param>
/// <param name="Amount">Signed amount. Top-up is positive, charge is negative.</param>
/// <param name="IdempotencyKey">Unique idempotency key.</param>
/// <param name="Reason">Human-readable reason.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
public sealed record LedgerEntry(
    string Id,
    string UserId,
    LedgerEntryType Type,
    MoneyAmount Amount,
    string IdempotencyKey,
    string Reason,
    DateTimeOffset CreatedAtUtc);

