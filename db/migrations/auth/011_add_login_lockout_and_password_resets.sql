-- Login brute-force protection: per-account failed-attempt counter and lockout
-- deadline. Both reset on successful login and on password change/reset.
alter table auth.users
    add column if not exists failed_login_count int not null default 0;

alter table auth.users
    add column if not exists locked_until_utc timestamptz;

-- Password reset challenge: same shape as auth.email_verifications (6-digit
-- code hash, expiry, resend cooldown, attempt counter), one pending row per
-- user. Deleted when the password is successfully reset.
create table if not exists auth.password_resets (
    user_id varchar(64) primary key references auth.users(id) on delete cascade,
    code_hash varchar(128) not null,
    expires_at_utc timestamptz not null,
    sent_at_utc timestamptz not null,
    attempts int not null default 0
);
