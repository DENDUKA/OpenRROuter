create schema if not exists auth;

create table if not exists auth.users (
    id varchar(64) primary key,
    email varchar(320) not null,
    role varchar(32) not null,
    status varchar(32) not null,
    password_hash varchar(512) not null,
    created_at_utc timestamptz not null
);

create unique index if not exists ux_auth_users_email
    on auth.users (email);

create index if not exists ix_auth_users_email_lower
    on auth.users (lower(email));

create table if not exists auth.invites (
    id varchar(64) primary key,
    email varchar(320) not null,
    role varchar(32) not null,
    token_hash varchar(128) not null,
    expires_at_utc timestamptz not null,
    created_by_admin_id varchar(64) not null,
    created_at_utc timestamptz not null,
    used_at_utc timestamptz null
);

create unique index if not exists ux_auth_invites_token_hash
    on auth.invites (token_hash);

create index if not exists ix_auth_invites_email_used_at
    on auth.invites (email, used_at_utc);

create table if not exists auth.api_keys (
    id varchar(64) primary key,
    user_id varchar(64) not null references auth.users(id),
    name varchar(128) not null,
    prefix varchar(32) not null,
    secret_hash varchar(128) not null,
    created_at_utc timestamptz not null,
    last_used_at_utc timestamptz null,
    revoked_at_utc timestamptz null
);

create index if not exists ix_auth_api_keys_user_id
    on auth.api_keys (user_id);

create index if not exists ix_auth_api_keys_prefix
    on auth.api_keys (prefix);

create unique index if not exists ux_auth_api_keys_secret_hash
    on auth.api_keys (secret_hash);
