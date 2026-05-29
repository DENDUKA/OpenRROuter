update billing.ledger_entries
set currency = 'Usd'
where currency is null
   or upper(currency) in ('RUB', 'USD');

update billing.model_prices
set currency = 'Usd'
where currency is null
   or upper(currency) in ('RUB', 'USD');

update billing.usage_records
set cost_currency = 'Usd'
where cost_currency is null
   or upper(cost_currency) in ('RUB', 'USD');

alter table billing.ledger_entries
    alter column currency set not null,
    drop constraint if exists ck_billing_ledger_entries_currency_usd,
    add constraint ck_billing_ledger_entries_currency_usd
        check (currency = 'Usd');

alter table billing.model_prices
    alter column currency set not null,
    drop constraint if exists ck_billing_model_prices_currency_usd,
    add constraint ck_billing_model_prices_currency_usd
        check (currency = 'Usd');

alter table billing.usage_records
    alter column cost_currency set not null,
    drop constraint if exists ck_billing_usage_records_cost_currency_usd,
    add constraint ck_billing_usage_records_cost_currency_usd
        check (cost_currency = 'Usd');
