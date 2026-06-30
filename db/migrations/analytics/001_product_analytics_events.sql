create schema if not exists analytics;

create table if not exists analytics.events (
    id              bigint generated always as identity primary key,
    event_name      text        not null,
    distinct_id     text        not null,
    occurred_at_utc timestamptz not null default now(),
    dedup_key       text        null,
    properties      jsonb       not null default '{}'::jsonb,
    schema_version  int         not null,
    environment     text        not null
);

-- Idempotency: redelivery of the same business event must not create a duplicate row.
create unique index if not exists ux_analytics_events_dedup
    on analytics.events (event_name, dedup_key) where dedup_key is not null;

create index if not exists ix_analytics_events_distinct
    on analytics.events (distinct_id);

create index if not exists ix_analytics_events_name_time
    on analytics.events (event_name, occurred_at_utc);
