-- Fix: case-insensitive unique constraint on email.
-- The existing ux_auth_users_email is case-sensitive, which allows
-- 'User@Example.com' and 'user@example.com' as distinct rows.
-- Replace with a lower(email) unique index.

DROP INDEX IF EXISTS auth.ux_auth_users_email;

CREATE UNIQUE INDEX IF NOT EXISTS ux_auth_users_email_lower
    ON auth.users (lower(email));
