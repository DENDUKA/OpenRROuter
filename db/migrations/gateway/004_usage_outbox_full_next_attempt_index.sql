-- Replace the partial index on next_attempt_at_utc with a full one.
--
-- ClaimPendingAsync filters by:
--     next_attempt_at_utc <= @Now
--   and (leased_until_utc is null or leased_until_utc < @Now)
--
-- The original partial index (migration 001) only covers rows where
-- leased_until_utc is null, so rows whose lease has expired but is not null
-- fall outside the index and the planner degrades to a seq scan on the
-- dispatcher's hot path (polled every ~2s). A full index on
-- next_attempt_at_utc covers both branches of the lease predicate.

DROP INDEX IF EXISTS gateway.ix_usage_outbox_next_attempt_at_utc;

CREATE INDEX IF NOT EXISTS ix_usage_outbox_next_attempt_at_utc
    ON gateway.usage_outbox (next_attempt_at_utc);
