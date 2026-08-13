# Balance and Billing

Rawrter uses a unified prepaid billing system in **US Dollars (USD)**.

## How Charging Works

Funds are deducted automatically after each successful request to a model based on actual Token Usage.

Cost calculation formula:

`Cost = (InputTokens * InputPrice + OutputTokens * OutputPrice + CacheTokens * CachePrice) / 1,000,000`

### Token Accounting Details

- **Caching:** If the model supports caching (e.g., Anthropic Prompt Caching), tokens that hit the cache are billed at a special (usually discounted) `CachePrice`. These tokens are subtracted from total `InputTokens` when calculating the base price cost.
- **Rounding:** Rounding is done to 6 decimal places (microcents).

## Topping Up Balance

You can top up your balance via the dashboard. Conversion to USD happens at the current exchange rate at the time of crediting.

In the dashboard, you can choose a convenient Display Currency, for example, rubles (RUB) or euros (EUR) — the recalculation happens automatically using current exchange rates.

## Zero Balance Errors

If your balance drops below zero, the API will start returning an `insufficient_balance` error (HTTP 402 / 400).