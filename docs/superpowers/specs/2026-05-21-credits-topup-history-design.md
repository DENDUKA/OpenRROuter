# Credits Top-Up History

**Date:** 2026-05-21
**Status:** Approved

## Goal

Display the full top-up history for the current user on the `/workspace/credits` page.

## Scope

Show all `LedgerEntryType.VirtualTopUp` entries belonging to the authenticated user, ordered by date descending. Columns: Date, Amount ($), Reason.

## Changes

### billing.proto

New RPC and messages:

```protobuf
rpc ListTopUps (ListTopUpsRequest) returns (ListTopUpsResponse);

message ListTopUpsRequest {
  string user_id = 1;
}

message TopUpRecord {
  string id = 1;
  string amount_usd = 2;
  string reason = 3;
  string created_at_utc = 4;
}

message ListTopUpsResponse {
  repeated TopUpRecord records = 1;
}
```

### BillingService

- `IBillingRepository`: add `ListTopUps(string userId, int limit)` returning `(IReadOnlyCollection<LedgerEntry>, bool HasMore)`.
- `BillingRepository` (Dapper/PostgreSQL): SQL query on `billing.ledger_entries` filtered by `user_id` and `type = 'VirtualTopUp'`, ordered by `created_at_utc desc`.
- `InMemoryBillingRepository`: equivalent LINQ query.
- `BillingApplicationService`: add `ListTopUps(string userId)` — applies pagination limit and delegates to repository.
- `GrpcBillingService`: implement `ListTopUps` RPC — maps `LedgerEntry` to `TopUpRecord`.

### WebApp

- `TopUpInfo` record DTO: `(decimal Amount, string Reason, DateTimeOffset CreatedAtUtc)`.
- `BillingClient`: add `ListTopUpsAsync(string userId)` — calls `ListTopUps` gRPC, maps to `TopUpInfo`.
- `Credits.cshtml.cs`: load `TopUps` list in `OnGetAsync`, handle errors gracefully.
- `Credits.cshtml`: add table with columns Date (local time, format `g`), Amount (`N2 $`), Reason. Show "No top-ups yet." when list is empty.

## Out of Scope

- Pagination UI (backend returns up to `MaxPageSize` records, same as other list endpoints)
- Correction/UsageCharge entries (not shown here)
