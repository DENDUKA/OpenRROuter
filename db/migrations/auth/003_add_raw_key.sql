alter table auth.api_keys
    add column if not exists raw_key varchar(128) null;
