alter table billing.model_prices
    add column if not exists reasoning_efforts_overridden boolean not null default false;
