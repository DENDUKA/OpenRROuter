-- Per-language model descriptions.
-- Single `description` column is replaced with `description_ru` / `description_en`.
-- Any existing values (always English — sourced from OpenRouter) are backfilled into `description_en`.
-- The old `description_source` discriminator still applies: it gates whether an enrich overwrites
-- per-language fields, and now covers all of them collectively.

alter table billing.model_prices
    add column if not exists description_ru text,
    add column if not exists description_en text;

update billing.model_prices
   set description_en = description
 where description_en is null
   and description is not null;

alter table billing.model_prices
    drop column if exists description;
