-- db/migrations/billing/011_model_tags.sql
CREATE TABLE billing.tags (
    id VARCHAR(50) PRIMARY KEY,
    display_name VARCHAR(100) NOT NULL,
    color VARCHAR(20) NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE billing.model_prices ADD COLUMN tag_ids text[] DEFAULT '{}';
