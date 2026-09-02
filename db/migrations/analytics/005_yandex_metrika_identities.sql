create table if not exists analytics.yandex_metrika_identities (
    user_id            varchar(64) primary key,
    client_id          varchar(32) not null,
    first_seen_at_utc  timestamptz not null,
    last_seen_at_utc   timestamptz not null,
    constraint ck_yandex_metrika_identity_client_id
        check (client_id ~ '^[0-9]{1,32}$'),
    constraint ck_yandex_metrika_identity_seen_order
        check (last_seen_at_utc >= first_seen_at_utc)
);

create index if not exists ix_yandex_metrika_identities_last_seen
    on analytics.yandex_metrika_identities (last_seen_at_utc);
