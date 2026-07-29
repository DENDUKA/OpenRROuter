-- T-028: Denormalized user balances to eliminate O(n) ledger aggregation
-- and race condition between balance check and debit.

create table billing.user_balances (
    user_id        text primary key,
    balance_amount numeric(18,6) not null default 0,
    updated_at_utc timestamptz   not null default now()
);

-- Populate from existing ledger (one-time backfill).
insert into billing.user_balances (user_id, balance_amount, updated_at_utc)
select user_id, coalesce(sum(amount), 0), now()
from billing.ledger_entries
group by user_id;
