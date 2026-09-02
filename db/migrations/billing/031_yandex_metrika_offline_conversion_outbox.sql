create table if not exists billing.yandex_metrika_offline_conversion_outbox (
    id                  varchar(64) primary key,
    dedup_key           varchar(160) not null,
    user_id             varchar(64) not null,
    target              varchar(64) not null,
    occurred_at_utc     timestamptz not null,
    price               numeric(18, 6) null,
    currency            varchar(8) null,
    resolved_client_id  varchar(32) null,
    status              varchar(16) not null default 'Pending',
    lease_owner         varchar(64) null,
    lease_until_utc     timestamptz null,
    attempts            integer not null default 0,
    next_attempt_at_utc timestamptz not null,
    upload_id           bigint null,
    upload_status       varchar(32) null,
    comment             varchar(255) not null,
    last_error          varchar(512) null,
    created_at_utc      timestamptz not null,
    updated_at_utc      timestamptz not null,
    constraint ux_yandex_metrika_offline_conversion_dedup unique (dedup_key),
    constraint ux_yandex_metrika_offline_conversion_comment unique (comment),
    constraint ck_yandex_metrika_offline_conversion_status
        check (status in ('Pending', 'Reconciling', 'Uploaded', 'Processed', 'Failed', 'Expired')),
    constraint ck_yandex_metrika_offline_conversion_target
        check (target in ('topup_completed', 'first_api_request')),
    constraint ck_yandex_metrika_offline_conversion_attempts check (attempts >= 0),
    constraint ck_yandex_metrika_offline_conversion_value
        check ((price is null and currency is null) or (price is not null and currency is not null)),
    constraint ck_yandex_metrika_offline_conversion_client_id
        check (resolved_client_id is null or resolved_client_id ~ '^[0-9]{1,32}$')
);

create index if not exists ix_yandex_metrika_offline_conversion_due
    on billing.yandex_metrika_offline_conversion_outbox (next_attempt_at_utc, occurred_at_utc)
    where status in ('Pending', 'Reconciling', 'Uploaded');
