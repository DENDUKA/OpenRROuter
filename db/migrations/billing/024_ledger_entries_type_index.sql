-- Composite index for ListAllTopUpsAsync: covers the WHERE type IN (...) filter
-- and the ORDER BY created_at_utc DESC, id DESC sort. Without this, every page
-- of /admin/topups triggers a sequential scan on the full ledger_entries table
-- (which grows by one row per API request).
CREATE INDEX IF NOT EXISTS ix_billing_ledger_entries_type_created_at_id
    ON billing.ledger_entries (type, created_at_utc DESC, id DESC);
