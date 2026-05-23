alter table billing.usage_records
    add column if not exists provider_name    varchar(64),
    add column if not exists provider_account varchar(256);

alter table billing.activity_by_token
    add column if not exists provider_name    varchar(64),
    add column if not exists provider_account varchar(256);
