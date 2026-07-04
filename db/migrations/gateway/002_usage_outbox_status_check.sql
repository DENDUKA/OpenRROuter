-- Enforce valid status values on usage_outbox.
ALTER TABLE gateway.usage_outbox
    ADD CONSTRAINT ck_usage_outbox_status
    CHECK (status IN ('pending', 'processing', 'completed', 'failed'));
