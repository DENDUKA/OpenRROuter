-- billing.model_providers: admin-managed provider directory
CREATE TABLE IF NOT EXISTS billing.model_providers (
    id              text        PRIMARY KEY,
    display_name    text        NOT NULL,
    description_ru  text,
    description_en  text,
    logo_bytes      bytea,
    logo_content_type text,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz NOT NULL DEFAULT now()
);

-- provider_id FK on model_prices (nullable, for backward compat)
ALTER TABLE billing.model_prices
    ADD COLUMN IF NOT EXISTS provider_id text;

-- Migrate distinct existing provider strings into model_providers
WITH normalized AS (
    SELECT
        trim(provider) AS display_name,
        trim(both '-' from regexp_replace(
            regexp_replace(lower(trim(provider)), '[^a-z0-9_./-]+', '-', 'g'),
            '-+', '-', 'g')) AS id
    FROM billing.model_prices
    WHERE provider IS NOT NULL AND trim(provider) <> ''
)
INSERT INTO billing.model_providers (id, display_name, created_at_utc, updated_at_utc)
SELECT DISTINCT ON (id)
       id,
       display_name,
       now(),
       now()
FROM normalized
WHERE id <> ''
ORDER BY id, display_name
ON CONFLICT (id) DO NOTHING;

-- Backfill provider_id from the migrated rows
WITH normalized AS (
    SELECT
        model_id,
        trim(both '-' from regexp_replace(
            regexp_replace(lower(trim(provider)), '[^a-z0-9_./-]+', '-', 'g'),
            '-+', '-', 'g')) AS provider_id
    FROM billing.model_prices
    WHERE provider IS NOT NULL AND trim(provider) <> ''
)
UPDATE billing.model_prices mp
SET provider_id = normalized.provider_id
FROM normalized
WHERE normalized.model_id = mp.model_id
  AND normalized.provider_id <> ''
  AND mp.provider_id IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_model_prices_provider_id'
    ) THEN
        ALTER TABLE billing.model_prices
            ADD CONSTRAINT fk_model_prices_provider_id
            FOREIGN KEY (provider_id) REFERENCES billing.model_providers(id)
            ON DELETE SET NULL;
    END IF;
END $$;

-- Index for fast lookups
CREATE INDEX IF NOT EXISTS idx_model_prices_provider_id
    ON billing.model_prices (provider_id);
