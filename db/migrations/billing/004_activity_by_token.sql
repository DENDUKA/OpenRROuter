create table if not exists billing.activity_by_token (
    request_id varchar(128) primary key,
    api_key_id varchar(64) not null,
    api_key_prefix varchar(32) not null,
    model_id varchar(128) not null,
    router_node_id varchar(64) not null,
    input_tokens bigint not null,
    output_tokens bigint not null,
    cache_tokens bigint not null,
    status varchar(32) not null,
    created_at_utc timestamptz not null,
    completed_at_utc timestamptz not null
);

create index if not exists ix_billing_activity_by_token_key_created
    on billing.activity_by_token (api_key_id, created_at_utc desc);

create index if not exists ix_billing_activity_by_token_created
    on billing.activity_by_token (created_at_utc desc);
