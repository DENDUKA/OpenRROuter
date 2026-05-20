create index if not exists ix_billing_usage_records_created_at
    on billing.usage_records (created_at_utc);

create index if not exists ix_billing_ledger_entries_user_amount
    on billing.ledger_entries (user_id, amount);
