-- Rename the API key revocation timestamp to match the revoke→disable rename in
-- the domain/application/repository layer. The column semantics are unchanged:
-- null = active, set = disabled (when). Renaming the column automatically
-- rewrites the dependent partial index predicate from migration 004
-- (ix_auth_api_keys_active_created ... where revoked_at_utc is null).
do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'auth'
          and table_name = 'api_keys'
          and column_name = 'revoked_at_utc'
    ) then
        alter table auth.api_keys rename column revoked_at_utc to disabled_at_utc;
    end if;
end $$;
