create schema if not exists gateway;

create table if not exists gateway.usage_outbox (
    request_id varchar(128) primary key,
    payload_json text not null,
    status varchar(32) not null,
    attempts integer not null,
    next_attempt_at_utc timestamptz not null,
    locked_by varchar(128) null,
    locked_until_utc timestamptz null,
    last_error text null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null
);

create index if not exists ix_gateway_usage_outbox_due
    on gateway.usage_outbox (status, next_attempt_at_utc);
