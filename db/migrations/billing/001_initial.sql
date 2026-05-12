create schema if not exists billing;

create table if not exists billing.ledger_entries (
    id varchar(64) primary key,
    user_id varchar(64) not null,
    type varchar(32) not null,
    amount numeric(18, 6) not null,
    currency varchar(8) not null,
    idempotency_key varchar(128) not null,
    reason varchar(512) not null,
    created_at_utc timestamptz not null
);

create index if not exists ix_billing_ledger_entries_user_id
    on billing.ledger_entries (user_id);

create unique index if not exists ux_billing_ledger_entries_idempotency_key
    on billing.ledger_entries (idempotency_key);

create table if not exists billing.model_prices (
    model_id varchar(128) primary key,
    display_name varchar(256) not null,
    router_model_id varchar(256) not null,
    price_version varchar(64) not null,
    input_per_million_amount numeric(18, 6) not null,
    output_per_million_amount numeric(18, 6) not null,
    cache_per_million_amount numeric(18, 6) not null,
    currency varchar(8) not null,
    enabled boolean not null
);

create index if not exists ix_billing_model_prices_enabled
    on billing.model_prices (enabled);

create table if not exists billing.usage_records (
    request_id varchar(128) primary key,
    user_id varchar(64) not null,
    api_key_id varchar(64) not null,
    model_id varchar(128) not null,
    price_version varchar(64) not null,
    router_node_id varchar(64) not null,
    input_tokens bigint not null,
    output_tokens bigint not null,
    cache_tokens bigint not null,
    cost_amount numeric(18, 6) not null,
    cost_currency varchar(8) not null,
    status varchar(32) not null,
    created_at_utc timestamptz not null
);

create index if not exists ix_billing_usage_records_user_id
    on billing.usage_records (user_id);

create index if not exists ix_billing_usage_records_model_created_at
    on billing.usage_records (model_id, created_at_utc);

create table if not exists billing.billing_anomalies (
    id varchar(64) primary key,
    request_id varchar(128) not null,
    type varchar(64) not null,
    message varchar(512) not null,
    created_at_utc timestamptz not null
);

create index if not exists ix_billing_anomalies_type_created_at
    on billing.billing_anomalies (type, created_at_utc);
