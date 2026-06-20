ALTER TABLE billing.model_prices
    ADD COLUMN IF NOT EXISTS model_type varchar(16) NOT NULL DEFAULT 'text';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_billing_model_prices_model_type'
    ) THEN
        ALTER TABLE billing.model_prices
            ADD CONSTRAINT ck_billing_model_prices_model_type
            CHECK (model_type IN ('text', 'audio'));
    END IF;
END $$;
