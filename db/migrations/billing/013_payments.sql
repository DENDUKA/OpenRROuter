create table if not exists billing.payments (
    id varchar(64) primary key,
    user_id varchar(64) not null,
    client_token varchar(64) not null,
    provider varchar(32) not null,
    provider_payment_id varchar(128) null,
    order_id varchar(64) not null,
    display_amount numeric(18,2) not null,
    display_currency varchar(8) not null,
    provider_amount numeric(18,2) not null,
    provider_currency varchar(8) not null,
    credited_usd_amount numeric(18,6) not null,
    fx_rate numeric(18,8) not null,
    fx_source varchar(16) not null,
    fx_at_utc timestamptz not null,
    status varchar(16) not null,
    cancel_reason varchar(256) null,
    pay_url varchar(512) null,
    lease_owner varchar(64) null,
    lease_until_utc timestamptz null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    expires_at_utc timestamptz null,
    confirmed_at_utc timestamptz null,
    canceled_at_utc timestamptz null,
    constraint ck_payments_amounts check (display_amount > 0 and provider_amount > 0
        and credited_usd_amount > 0 and fx_rate > 0),
    constraint ck_payments_status check (status in
        ('Created','Paid','Canceled','Expired','Refunded','ChargedBack'))
);

create unique index if not exists ux_billing_payments_provider_ref
    on billing.payments (provider, provider_payment_id) where provider_payment_id is not null;
create unique index if not exists ux_billing_payments_client_token
    on billing.payments (user_id, client_token);
create index if not exists ix_billing_payments_user_created
    on billing.payments (user_id, created_at_utc desc);
create index if not exists ix_billing_payments_reconcile
    on billing.payments (status, created_at_utc) where status = 'Created';

create table if not exists billing.payment_events (
    id varchar(64) primary key,
    payment_id varchar(64) not null,
    provider varchar(32) not null,
    provider_event_id varchar(128) null,
    provider_payment_id varchar(128) null,
    event_type varchar(32) not null,
    order_id varchar(64) null,
    confirmed_amount numeric(18,2) null,
    confirmed_currency varchar(8) null,
    reason varchar(256) null,
    raw_payload text null,
    signature_ok boolean not null,
    processed boolean not null default false,
    attempts int not null default 0,
    received_at_utc timestamptz not null,
    processed_at_utc timestamptz null
);

create unique index if not exists ux_payment_events_provider_event
    on billing.payment_events (provider, provider_event_id) where provider_event_id is not null;
create unique index if not exists ux_payment_events_fallback
    on billing.payment_events (payment_id, event_type) where provider_event_id is null;
