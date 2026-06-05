-- Soft-delete marker for API keys. A key may be deleted (hidden from the user's
-- list) only after it has been revoked. Mirrors the existing revoked_at_utc /
-- last_used_at_utc nullable-timestamp pattern: null = visible, set = deleted (when).
alter table auth.api_keys
    add column if not exists deleted_at_utc timestamptz null;

-- Workspace list filters on user_id and excludes deleted rows.
create index if not exists ix_auth_api_keys_user_not_deleted
    on auth.api_keys (user_id, created_at_utc desc)
    where deleted_at_utc is null;
