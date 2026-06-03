-- Self-serve signup email confirmation: one pending challenge per not-yet-verified
-- user. The row is deleted when the account is activated (status -> 'Active').
create table if not exists auth.email_verifications (
    user_id varchar(64) primary key references auth.users(id) on delete cascade,
    code_hash varchar(128) not null,
    expires_at_utc timestamptz not null,
    sent_at_utc timestamptz not null,
    attempts int not null default 0
);
