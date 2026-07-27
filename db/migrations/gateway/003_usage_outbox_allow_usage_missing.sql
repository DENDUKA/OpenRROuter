-- Keep the outbox schema aligned with handlers that record a response without token usage.
ALTER TABLE gateway.usage_outbox
    DROP CONSTRAINT ck_usage_outbox_status;

ALTER TABLE gateway.usage_outbox
    ADD CONSTRAINT ck_usage_outbox_status
    CHECK (status IN ('pending', 'processing', 'completed', 'failed', 'usage_missing'));
