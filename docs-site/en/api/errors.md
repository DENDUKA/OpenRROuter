# Error Handling

In case of an error, the API returns a response in standard JSON format with an appropriate HTTP status (4xx or 5xx).

## Error Format

```json
{
  "error": {
    "code": "insufficient_balance",
    "message": "Not enough funds on balance to perform the request.",
    "type": "billing_error"
  }
}
```

## Standard Error Codes

| Error Code | Description |
|---|---|
| `authentication_required` | Missing Authorization header |
| `invalid_api_key` | Invalid or revoked API key |
| `insufficient_balance` | Negative balance (for paid models) |
| `api_key_limit_exceeded` | Spending limit exceeded for this key |
| `model_unavailable` | Requested model is disabled or not found |
| `router_unavailable` | No available nodes to process request (upstream) |
| `validation_failed` | JSON request body validation error |
| `rate_limited` | Too many requests |
| `internal_error` | Internal platform error |

## Upstream Errors

If an error occurred on the model provider's side (e.g., OpenAI or Anthropic) after the gateway successfully routed the request, Rawrter proxies this error back to the client in its original form.