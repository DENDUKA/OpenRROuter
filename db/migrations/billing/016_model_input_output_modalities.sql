ALTER TABLE billing.model_prices
    ADD COLUMN IF NOT EXISTS input_modalities text,
    ADD COLUMN IF NOT EXISTS output_modalities text;

UPDATE billing.model_prices
SET input_modalities = modalities,
    output_modalities = 'text'
WHERE modalities IS NOT NULL
  AND input_modalities IS NULL;
