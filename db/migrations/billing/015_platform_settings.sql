-- Generic key/value store for runtime-editable platform settings (admin-configurable),
-- e.g. the public API base URL shown in the Setup guide. Kept tiny and untyped on purpose.
create table if not exists billing.platform_settings (
    key            text primary key,
    value          text not null,
    updated_at_utc timestamptz not null default now()
);
