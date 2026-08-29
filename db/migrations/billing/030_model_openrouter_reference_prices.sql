alter table billing.model_prices
    add column if not exists openrouter_input_per_million_amount numeric(18, 6),
    add column if not exists openrouter_output_per_million_amount numeric(18, 6),
    add column if not exists openrouter_cache_per_million_amount numeric(18, 6);

alter table billing.model_prices
    drop constraint if exists ck_model_prices_openrouter_input_non_negative,
    add constraint ck_model_prices_openrouter_input_non_negative
        check (openrouter_input_per_million_amount is null or openrouter_input_per_million_amount >= 0),
    drop constraint if exists ck_model_prices_openrouter_output_non_negative,
    add constraint ck_model_prices_openrouter_output_non_negative
        check (openrouter_output_per_million_amount is null or openrouter_output_per_million_amount >= 0),
    drop constraint if exists ck_model_prices_openrouter_cache_non_negative,
    add constraint ck_model_prices_openrouter_cache_non_negative
        check (openrouter_cache_per_million_amount is null or openrouter_cache_per_million_amount >= 0);
