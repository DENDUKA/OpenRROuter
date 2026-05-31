alter table billing.model_prices
    add column if not exists provider varchar(200);
