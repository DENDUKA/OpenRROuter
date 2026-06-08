alter table billing.model_prices
    add column if not exists supports_reasoning boolean not null default false,
    add column if not exists allowed_reasoning_efforts text not null default '',
    add column if not exists default_reasoning_effort text null;
