-- Composite index for ListUsageAsync keyset pagination:
-- WHERE user_id = @UserId
--   AND (created_at_utc, request_id) < (@AfterCreatedAt, @AfterRequestId)
-- ORDER BY created_at_utc DESC, request_id DESC
CREATE INDEX IF NOT EXISTS ix_billing_usage_records_user_created_request
    ON billing.usage_records (user_id, created_at_utc DESC, request_id DESC);
