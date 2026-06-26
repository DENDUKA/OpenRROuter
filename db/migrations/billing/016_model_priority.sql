alter table billing.model_prices
    add column if not exists priority integer not null default 0;
