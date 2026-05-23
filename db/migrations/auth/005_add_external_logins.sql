alter table auth.users
    alter column password_hash drop not null;

create table auth.external_logins (
    id             varchar(64)  primary key,
    user_id        varchar(64)  not null references auth.users(id),
    provider       varchar(64)  not null,
    provider_sub   varchar(256) not null,
    created_at_utc timestamptz  not null
);

create unique index ux_auth_external_logins_provider_sub
    on auth.external_logins(provider, provider_sub);

create index ix_auth_external_logins_user_id
    on auth.external_logins(user_id);
