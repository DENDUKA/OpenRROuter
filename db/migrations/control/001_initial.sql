create schema if not exists control;

create table if not exists control.router_nodes (
    id varchar(64) primary key,
    name varchar(128) not null,
    endpoint_url varchar(512) not null,
    token_secret_name varchar(128) not null,
    capacity_weight integer not null,
    enabled boolean not null,
    status varchar(32) not null,
    in_flight_requests integer not null,
    last_checked_at_utc timestamptz null,
    last_health_latency_ms integer null,
    last_health_error_code varchar(128) null,
    created_at_utc timestamptz not null
);

create index if not exists ix_control_router_nodes_enabled
    on control.router_nodes (enabled);

create index if not exists ix_control_router_nodes_status
    on control.router_nodes (status);

create table if not exists control.router_leases (
    id varchar(64) primary key,
    node_id varchar(64) not null references control.router_nodes(id),
    request_id varchar(128) not null,
    expires_at_utc timestamptz not null,
    created_at_utc timestamptz not null
);

create index if not exists ix_control_router_leases_node_id
    on control.router_leases (node_id);

create index if not exists ix_control_router_leases_request_id
    on control.router_leases (request_id);

create index if not exists ix_control_router_leases_expires_at
    on control.router_leases (expires_at_utc);
