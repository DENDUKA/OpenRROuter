create table if not exists billing.api_key_spend_limits (
    api_key_id varchar(64) primary key,
    user_id varchar(64) not null,
    daily_limit_amount numeric(18, 6) null,
    weekly_limit_amount numeric(18, 6) null,
    currency varchar(8) not null default 'Usd',
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    constraint ck_billing_api_key_spend_limits_currency_usd check (currency = 'Usd'),
    constraint ck_billing_api_key_spend_limits_daily_positive check (daily_limit_amount is null or daily_limit_amount > 0),
    constraint ck_billing_api_key_spend_limits_weekly_positive check (weekly_limit_amount is null or weekly_limit_amount > 0)
);

create index if not exists ix_billing_api_key_spend_limits_user
    on billing.api_key_spend_limits (user_id);

create index if not exists ix_billing_usage_records_key_created
    on billing.usage_records (api_key_id, created_at_utc);
