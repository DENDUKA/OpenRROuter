create schema if not exists gateway;

create table if not exists gateway.usage_outbox (
    request_id text primary key,
    user_id text not null,
    api_key_id text not null,
    api_key_prefix text not null,
    model_id text not null,
    router_node_id text not null,
    input_tokens bigint not null,
    output_tokens bigint not null,
    cache_tokens bigint not null,
    status text not null,
    started_at_utc timestamptz not null,
    completed_at_utc timestamptz not null,
    provider_name text null,
    provider_account text null,
    endpoint text null,
    stream boolean not null default false,
    attempts int not null default 0,
    next_attempt_at_utc timestamptz not null default now(),
    leased_until_utc timestamptz null,
    created_at_utc timestamptz not null default now()
);

create index if not exists ix_usage_outbox_next_attempt_at_utc
    on gateway.usage_outbox (next_attempt_at_utc)
    where leased_until_utc is null;
