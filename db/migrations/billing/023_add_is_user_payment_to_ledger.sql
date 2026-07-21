ALTER TABLE billing.ledger_entries
    ADD COLUMN IF NOT EXISTS is_user_payment boolean NOT NULL DEFAULT false;

UPDATE billing.ledger_entries
SET is_user_payment = true
WHERE type = 'ExternalTopUp'
  AND idempotency_key NOT LIKE 'promo1:%'
  AND is_user_payment = false;

CREATE INDEX IF NOT EXISTS ix_billing_ledger_entries_user_is_user_payment
    ON billing.ledger_entries (user_id) WHERE is_user_payment = true;
