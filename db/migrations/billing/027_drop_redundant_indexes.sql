-- Drop two redundant indexes that cause unnecessary write amplification on the
-- two hottest tables in the billing schema (one row per API request each).
--
-- 1. ix_billing_ledger_entries_user_id_incl_amount (migration 021)
--    Was added "for GetBalanceAsync (index-only scan)", but after migration 026
--    introduced billing.user_balances, GetBalanceAsync reads the balance from
--    that denormalized table, not from ledger_entries. No remaining query uses
--    this index; it only slows down every ledger insert.
--
-- 2. ix_billing_usage_records_user_id (migration 001)
--    A plain (user_id) index. Migration 025 added
--    ix_billing_usage_records_user_created_request
--    (user_id, created_at_utc DESC, request_id DESC), whose leftmost prefix
--    covers every (user_id = @UserId) lookup. The old single-column index is
--    therefore redundant and only adds bloat to the most write-heavy table.

DROP INDEX IF EXISTS billing.ix_billing_ledger_entries_user_id_incl_amount;
DROP INDEX IF EXISTS billing.ix_billing_usage_records_user_id;
