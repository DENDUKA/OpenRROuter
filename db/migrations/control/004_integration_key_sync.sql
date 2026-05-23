create table control.integration_key_sync (
    api_key_id  text        not null,
    node_id     text        not null references control.router_nodes(id) on delete cascade,
    router_key_id text      not null,
    pushed_at_utc timestamptz not null default now(),
    primary key (api_key_id, node_id)
);

create index ix_integration_key_sync_api_key_id on control.integration_key_sync (api_key_id);
