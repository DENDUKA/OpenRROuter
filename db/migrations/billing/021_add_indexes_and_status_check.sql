-- Performance indexes for hot-path queries.

-- activity_by_token: ListActivityByTokenAsync, GetActivitySummaryAsync
-- filter by api_key_id = any(@ApiKeyIds), order by created_at_utc desc
CREATE INDEX IF NOT EXISTS ix_billing_activity_by_token_api_key_created
    ON billing.activity_by_token (api_key_id, created_at_utc DESC);

-- payments: AcquireStalePaymentsAsync
-- filter by status = 'Created' and created_at_utc < @OlderThanUtc
CREATE INDEX IF NOT EXISTS ix_billing_payments_status_created
    ON billing.payments (status, created_at_utc);

-- ledger_entries: GetBalanceAsync (index-only scan)
CREATE INDEX IF NOT EXISTS ix_billing_ledger_entries_user_id_incl_amount
    ON billing.ledger_entries (user_id) INCLUDE (amount);

-- ledger_entries: ListTopUpsAsync (filter + sort)
CREATE INDEX IF NOT EXISTS ix_billing_ledger_entries_user_created
    ON billing.ledger_entries (user_id, created_at_utc DESC);
