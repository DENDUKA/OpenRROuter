ALTER TABLE billing.payments
    ADD COLUMN IF NOT EXISTS bonus_percent numeric(5,2) null,
    ADD COLUMN IF NOT EXISTS bonus_usd numeric(12,6) null;
