-- Fix pricing for xiaomi/mimo-v2.5-pro and recalculate history
-- Correct OpenRouter prices: input $0.435, output $0.87, cache $0.0036 per 1M tokens

-- 1. Update model_prices
UPDATE billing.model_prices
SET
    input_per_million_amount = 0.435,
    output_per_million_amount = 0.87,
    cache_per_million_amount = 0.0036,
    price_version = 'openrouter-2026-06-15'
WHERE model_id = 'xiaomi/mimo-v2.5-pro';

-- 2. Recalculate usage_records cost_amount using the new prices
-- Formula matches UsageCostCalculator: billableInput = max(0, input - cache)
WITH new_prices AS (
    SELECT
        input_per_million_amount AS input_price,
        output_per_million_amount AS output_price,
        cache_per_million_amount AS cache_price
    FROM billing.model_prices
    WHERE model_id = 'xiaomi/mimo-v2.5-pro'
),
recalc AS (
    SELECT
        u.request_id,
        u.user_id,
        ROUND(
            (GREATEST(0, u.input_tokens - u.cache_tokens) * np.input_price
             + u.output_tokens * np.output_price
             + u.cache_tokens * np.cache_price) / 1000000.0,
            6
        ) AS new_cost
    FROM billing.usage_records u
    CROSS JOIN new_prices np
    WHERE u.model_id = 'xiaomi/mimo-v2.5-pro'
)
UPDATE billing.usage_records u
SET
    cost_amount = r.new_cost,
    price_version = 'openrouter-2026-06-15'
FROM recalc r
WHERE u.request_id = r.request_id;

-- 3. Recalculate ledger_entries for these usage records
-- Usage charges have negative amounts; idempotency_key = 'usage:' || request_id
WITH new_prices AS (
    SELECT
        input_per_million_amount AS input_price,
        output_per_million_amount AS output_price,
        cache_per_million_amount AS cache_price
    FROM billing.model_prices
    WHERE model_id = 'xiaomi/mimo-v2.5-pro'
),
usage_costs AS (
    SELECT
        u.request_id,
        u.user_id,
        ROUND(
            (GREATEST(0, u.input_tokens - u.cache_tokens) * np.input_price
             + u.output_tokens * np.output_price
             + u.cache_tokens * np.cache_price) / 1000000.0,
            6
        ) AS new_cost
    FROM billing.usage_records u
    CROSS JOIN new_prices np
    WHERE u.model_id = 'xiaomi/mimo-v2.5-pro'
)
UPDATE billing.ledger_entries le
SET amount = -uc.new_cost
FROM usage_costs uc
WHERE le.idempotency_key = 'usage:' || uc.request_id;
