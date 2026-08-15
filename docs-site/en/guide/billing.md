# Balance and Billing

Rawrter uses a unified prepaid billing system in **US Dollars (USD)**.

## How Charging Works

Funds are deducted automatically after each successful request to a model based on actual Token Usage.

Cost calculation formula:

`Cost = (InputTokens * InputPrice + OutputTokens * OutputPrice + CacheTokens * CachePrice) / 1,000,000`

### Token Accounting Details

- **Prompt Caching:** Leading models (Anthropic Claude, DeepSeek, OpenAI) support automatic prompt caching for repeated prefix contexts (system prompts, documentation files, long conversation histories). Tokens read from the cache are billed at a discounted `CachePrice` (often 2–10x cheaper than standard input tokens). In the billing formula, cached tokens are subtracted from total input tokens:
  ```
  Billable Input = max(0, InputTokens - CacheTokens)
  Cost = (Billable Input * InputPrice + OutputTokens * OutputPrice + CacheTokens * CachePrice) / 1,000,000
  ```
- **Rounding:** Rounding is done to 6 decimal places (microcents).
- **Free Models:** For models with zero input and output prices, requests are allowed even with a zero balance.

## Topping Up Balance

You can top up your balance via the dashboard. Conversion to USD happens at the current exchange rate at the time of crediting.

In the dashboard, you can choose a convenient Display Currency, for example, rubles (RUB) or euros (EUR) — the recalculation happens automatically using current exchange rates.

## Zero Balance Errors

If your balance drops below zero, the API will start returning an `insufficient_balance` error (HTTP 402 / 400).