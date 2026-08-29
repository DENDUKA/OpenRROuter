alter table billing.model_prices
    add column if not exists is_flagship boolean not null default false;
