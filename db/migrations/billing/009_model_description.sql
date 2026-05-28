alter table billing.model_prices
    add column if not exists description        text,
    add column if not exists context_length     integer,
    add column if not exists modalities         varchar(128),
    add column if not exists description_source varchar(32);
