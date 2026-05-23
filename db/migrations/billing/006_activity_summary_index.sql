create index if not exists ix_billing_activity_by_token_key_model_created
    on billing.activity_by_token (api_key_id, model_id, created_at_utc desc);
