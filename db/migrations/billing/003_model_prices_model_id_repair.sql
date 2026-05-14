do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'billing'
          and table_name = 'model_prices'
          and column_name = 'id'
    ) and not exists (
        select 1
        from information_schema.columns
        where table_schema = 'billing'
          and table_name = 'model_prices'
          and column_name = 'model_id'
    ) then
        alter table billing.model_prices rename column id to model_id;
    end if;
end $$;

alter table billing.model_prices
    add column if not exists model_id varchar(128),
    add column if not exists display_name varchar(256),
    add column if not exists router_model_id varchar(256),
    add column if not exists price_version varchar(64),
    add column if not exists input_per_million_amount numeric(18, 6),
    add column if not exists output_per_million_amount numeric(18, 6),
    add column if not exists cache_per_million_amount numeric(18, 6),
    add column if not exists currency varchar(8),
    add column if not exists enabled boolean;

create unique index if not exists ux_billing_model_prices_model_id
    on billing.model_prices (model_id);

create index if not exists ix_billing_model_prices_enabled_model_id
    on billing.model_prices (model_id)
    where enabled = true;
