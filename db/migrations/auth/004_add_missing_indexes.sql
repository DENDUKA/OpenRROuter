create index if not exists ix_auth_users_created_at
    on auth.users (created_at_utc desc);

create index if not exists ix_auth_api_keys_user_created
    on auth.api_keys (user_id, created_at_utc desc);

create index if not exists ix_auth_api_keys_active_created
    on auth.api_keys (created_at_utc desc)
    where revoked_at_utc is null;
