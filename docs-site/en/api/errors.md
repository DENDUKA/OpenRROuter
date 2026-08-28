---
title: "API Error Handling and Status Codes | Rawrter Docs"
description: "Comprehensive developer guide for Rawrter API error handling: JSON format, platform codes, retry strategies, exponential backoff, and code examples in Python, TypeScript, and cURL."
---

# Error Handling

The Rawrter API is fully compliant with the [OpenAI API Error Response](https://platform.openai.com/docs/guides/error-codes) specification. When an error occurs, the server returns a standard JSON response with an appropriate HTTP status code (4xx or 5xx).

Each error response includes detailed diagnostic information about the issue, a machine-readable error code, and a unique request identifier (`request_id`) for tracing and support inquiries.

---

## Error Response Format

Error responses are always structured as a top-level JSON object containing an `error` property:

```json
{
  "error": {
    "message": "Not enough funds on balance to perform the request.",
    "type": "billing_error",
    "code": "insufficient_balance",
    "param": null,
    "request_id": "0HN123456789A:00000001"
  }
}
```

### `error` Object Fields

| Field | Type | Description |
|---|---|---|
| `message` | `string` | Human-readable explanation of the error in English or Russian. |
| `type` | `string` | Category of the error (e.g., `invalid_request_error`, `authentication_error`, `billing_error`, `rate_limit_error`, `service_unavailable`, `internal_error`). |
| `code` | `string \| null` | Machine-readable error code defined by the platform or upstream provider (e.g., `insufficient_balance`, `invalid_api_key`, `model_unavailable`). |
| `param` | `string \| null` | The specific request parameter that caused the error, if applicable (e.g., `model`, `messages`, `temperature`). |
| `request_id` | `string \| null` | Unique Correlation ID for the request. Also reflected in the `X-Request-Id` and `X-Correlation-Id` response headers. |

---

## HTTP Status Codes & Error Codes

The table below lists all standard HTTP status codes returned by the Rawrter gateway, their corresponding platform error codes, root causes, and recommended client actions.

| HTTP Status | Error Code (`code`) | Error Type (`type`) | Cause | Recommended Client Action |
|---|---|---|---|---|
| **400** Bad Request | `validation_failed` | `invalid_request_error` | Malformed JSON, missing mandatory fields (`model`, `messages`), invalid data types, or conflicting parameters. | Check request syntax and parameter schemas. **Do not retry without fixing the payload.** |
| **401** Unauthorized | `authentication_required` | `authentication_error` | The `Authorization` header containing the Bearer token is missing. | Include the `Authorization: Bearer sk-or-...` header with a valid API key. |
| **401** Unauthorized | `invalid_api_key` | `authentication_error` | The provided API key is invalid, revoked, deleted, or suspended. | Verify your API key in the [dashboard](https://rawrter.com/en) and update your application configuration. |
| **402** Payment Required | `insufficient_balance` | `billing_error` | Account balance is zero or negative, or insufficient to cover the estimated cost for the requested model. | Top up your account balance in the Rawrter dashboard. |
| **402** Payment Required | `api_key_limit_exceeded` | `billing_error` | The configured spend limit for this specific API key has been exceeded. | Increase the key's spending limit in the dashboard or generate a new API key. |
| **403** Forbidden | `access_denied` / `forbidden` | `permission_error` | Access to the requested model or feature is restricted by account security policies. | Review permissions and settings in your account dashboard. |
| **404** Not Found | `model_unavailable` | `invalid_request_error` | The requested model (`model`) does not exist, is disabled, or the endpoint URL is incorrect. | Verify model IDs via `GET /v1/models` or refer to the [Models Overview](/en/guide/models). |
| **429** Too Many Requests | `rate_limited` | `rate_limit_error` | Request rate limit (RPM/RPS) exceeded for the API key or client IP address. | Pause sending requests for the duration indicated in the `Retry-After` header (in seconds) and implement exponential backoff. |
| **500** Internal Server Error | `internal_error` | `internal_error` | Unexpected internal gateway error while processing the request. | Retry the request with exponential backoff. If persistent, contact support with the `request_id`. |
| **502** Bad Gateway | `bad_gateway` | `service_unavailable` | Upstream provider connection failed or invalid response received from upstream node. | Retry after a brief delay (the gateway typically executes automatic failover to another node). |
| **503** Service Unavailable | `router_unavailable` | `service_unavailable` | No available router nodes or workers to handle requests for the requested model. | Retry with exponential backoff and jitter. |

---

## Retry Strategy & Exponential Backoff

When building production-ready LLM integrations, it is critical to distinguish between **transient** (retryable) and **permanent** (non-retryable) errors.

### Retryable vs. Non-Retryable Errors

- **Non-Retryable Errors (400, 401, 402, 403, 404)**:  
  Resending an identical request will always result in the same error. Automatic retries are **discouraged** — application code, API keys, account balances, or model IDs must be corrected first.
- **Retryable Errors (429, 500, 502, 503, 504)**:  
  Caused by temporary conditions (transient load spikes, upstream network hiccups, temporary node unavailability). These requests should be retried using **exponential backoff** and randomized **jitter**.

### Handling the `Retry-After` Header on 429

When the gateway returns a `429 Too Many Requests` status, it includes a `Retry-After` HTTP header indicating how many seconds to wait:

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 5
X-Request-Id: 0HN123456789A:00000001
```

The client should pause execution for the specified duration before attempting another request. If the header is missing, fallback to exponential backoff.

### Exponential Backoff Formula with Jitter

To prevent the thundering herd problem where many clients retry simultaneously, introduce random jitter:

$$\text{delay} = \min(\text{max\_delay}, \text{base\_delay} \times 2^{\text{attempt}}) + \text{jitter}$$

- $\text{base\_delay}$ — initial backoff delay (e.g., 1.0 second)
- $\text{attempt}$ — retry attempt number (0, 1, 2...)
- $\text{max\_delay}$ — maximum backoff ceiling (e.g., 30–60 seconds)
- $\text{jitter}$ — random delay between 0 and 500 ms

Recommended retry attempts: **3–5 attempts**.

---

## Practical Error Handling Examples

Below are production-grade error handling and retry examples across popular languages and runtimes.

::: code-group

```python [Python (OpenAI SDK)]
import time
import random
from openai import (
    OpenAI,
    APIError,
    AuthenticationError,
    RateLimitError,
    BadRequestError,
    APIConnectionError,
    InternalServerError,
)

client = OpenAI(
    api_key="sk-or-your-api-key",
    base_url="https://api.rawrter.com/v1",
)

def generate_chat_completion(messages, model="gpt-4o", max_retries=3):
    """
    Executes a Chat Completion request with typed error handling
    and exponential backoff for transient failures (429, 5xx, Network).
    """
    for attempt in range(max_retries):
        try:
            response = client.chat.completions.create(
                model=model,
                messages=messages,
            )
            return response.choices[0].message.content

        except AuthenticationError as e:
            # 401: Invalid API key or missing Authorization header
            print(f"[401] Authentication error: {e.message}")
            print("Check your sk-or-*** API key in the Rawrter dashboard.")
            raise

        except BadRequestError as e:
            # 400 / 404: Invalid parameters or unknown model
            print(f"[400/404] Bad request: {e.message} (code: {e.code}, param: {e.param})")
            raise

        except RateLimitError as e:
            # 429: Rate limit exceeded
            retry_after_header = getattr(e.response, "headers", {}).get("retry-after")
            if retry_after_header:
                delay = float(retry_after_header)
            else:
                delay = (2 ** attempt) + random.uniform(0.1, 0.5)

            print(f"[429] Rate limit hit. Retrying in {delay:.2f}s (attempt {attempt + 1}/{max_retries})...")
            time.sleep(delay)

        except APIConnectionError as e:
            # Network failure or socket reset
            delay = (2 ** attempt) + random.uniform(0.1, 0.5)
            print(f"[Network error] {e}. Retrying in {delay:.2f}s...")
            time.sleep(delay)

        except InternalServerError as e:
            # 500 / 502 / 503: Server error
            delay = (2 ** attempt) + random.uniform(0.1, 0.5)
            print(f"[{e.status_code}] Server error: {e.message}. Retrying in {delay:.2f}s...")
            time.sleep(delay)

        except APIError as e:
            # Other API errors (e.g., 402 Insufficient Balance)
            print(f"[{e.status_code}] API error: {e.message} (code: {e.code})")
            if e.status_code and e.status_code in (429, 500, 502, 503):
                delay = (2 ** attempt) + random.uniform(0.1, 0.5)
                time.sleep(delay)
            else:
                raise

    raise RuntimeError(f"Request failed after {max_retries} attempts.")

# Example usage
if __name__ == "__main__":
    try:
        reply = generate_chat_completion([{"role": "user", "content": "Hello, Rawrter!"}])
        print("Model response:", reply)
    except Exception as err:
        print("Request failed with fatal error:", err)
```

```typescript [TypeScript / Node.js]
import OpenAI, {
  APIError,
  AuthenticationError,
  BadRequestError,
  RateLimitError,
  InternalServerError,
  APIConnectionError,
} from 'openai';

const client = new OpenAI({
  apiKey: process.env.RAWRTER_API_KEY || 'sk-or-your-api-key',
  baseURL: 'https://api.rawrter.com/v1',
});

interface ChatOptions {
  model?: string;
  maxRetries?: number;
}

async function createChatCompletionWithRetry(
  messages: OpenAI.Chat.ChatCompletionMessageParam[],
  options: ChatOptions = {}
): Promise<string> {
  const { model = 'gpt-4o', maxRetries = 3 } = options;

  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      const response = await client.chat.completions.create({
        model,
        messages,
      });

      return response.choices[0]?.message?.content || '';
    } catch (error: any) {
      if (error instanceof AuthenticationError) {
        console.error(`[401] Authentication failed: ${error.message}`);
        throw error; // Non-retryable
      }

      if (error instanceof BadRequestError) {
        console.error(`[400] Bad request: ${error.message} (param: ${error.param}, code: ${error.code})`);
        throw error; // Non-retryable
      }

      if (error instanceof RateLimitError) {
        const retryAfter = error.headers?.['retry-after'];
        const delaySeconds = retryAfter ? parseFloat(retryAfter) : Math.pow(2, attempt) + Math.random() * 0.5;
        console.warn(`[429] Rate limit reached. Waiting ${delaySeconds.toFixed(1)}s before retry...`);
        await new Promise((resolve) => setTimeout(resolve, delaySeconds * 1000));
        continue;
      }

      if (error instanceof APIConnectionError) {
        const delayMs = Math.pow(2, attempt) * 1000 + Math.random() * 500;
        console.warn(`[Network error] ${error.message}. Retrying in ${Math.round(delayMs)}ms...`);
        await new Promise((resolve) => setTimeout(resolve, delayMs));
        continue;
      }

      if (error instanceof InternalServerError || (error instanceof APIError && (error.status === 502 || error.status === 503))) {
        const delayMs = Math.pow(2, attempt) * 1000 + Math.random() * 500;
        const reqId = error.headers?.['x-request-id'] || 'N/A';
        console.warn(`[${error.status}] Server error (Request ID: ${reqId}). Attempt ${attempt + 1}/${maxRetries} in ${Math.round(delayMs)}ms...`);
        await new Promise((resolve) => setTimeout(resolve, delayMs));
        continue;
      }

      if (error instanceof APIError) {
        console.error(`[${error.status}] API error [${error.code}]: ${error.message}`);
        throw error;
      }

      throw error;
    }
  }

  throw new Error(`Request failed after ${maxRetries} attempts.`);
}
```

```bash [cURL & Bash]
#!/usr/bin/env bash
set -e

API_KEY="sk-or-your-api-key"
ENDPOINT="https://api.rawrter.com/v1/chat/completions"

# Execute request capturing HTTP status and response body
RAW_RESPONSE=$(curl -s -w "\n%{http_code}" "$ENDPOINT" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d '{
    "model": "gpt-4o",
    "messages": [
      { "role": "user", "content": "Hello!" }
    ]
  }')

# Separate body and HTTP code
HTTP_BODY=$(echo "$RAW_RESPONSE" | sed '$d')
HTTP_CODE=$(echo "$RAW_RESPONSE" | tail -n1)

if [ "$HTTP_CODE" -ge 400 ]; then
  echo "❌ API Error (HTTP $HTTP_CODE):"
  
  # Parse JSON response using jq
  ERROR_TYPE=$(echo "$HTTP_BODY" | jq -r '.error.type // "unknown"')
  ERROR_CODE=$(echo "$HTTP_BODY" | jq -r '.error.code // "unknown"')
  ERROR_MSG=$(echo "$HTTP_BODY" | jq -r '.error.message // "No error message"')
  REQ_ID=$(echo "$HTTP_BODY" | jq -r '.error.request_id // "N/A"')

  echo "  Error Type:  $ERROR_TYPE"
  echo "  Error Code:  $ERROR_CODE"
  echo "  Message:     $ERROR_MSG"
  echo "  Request ID:  $REQ_ID"
  exit 1
else
  echo "✅ Successful Response (HTTP $HTTP_CODE):"
  echo "$HTTP_BODY" | jq -r '.choices[0].message.content'
fi
```

:::

---

## Upstream Provider Errors & Failover

The Rawrter gateway routes requests to distributed nodes and upstream model providers (OpenAI, Anthropic, Google, xAI, etc.).

### Transparent Error Pass-Through

If a request successfully passes authentication and gateway routing but fails during model execution on the upstream provider (for instance, `context_length_exceeded` or upstream safety `content_filter` triggers), Rawrter proxies the error back to the client in its original format and HTTP status code.

This guarantees complete drop-in compatibility: standard SDK exception handlers and parsing logic continue to work without modification.

### Automated Node Failover

To maximize reliability, Rawrter implements automatic node **Failover**:

1. If a selected node encounters a network reset, timeout, or returns a 5xx status code **before data streaming begins** (before the first byte is sent to the client), the gateway marks the node as failed.
2. The request is transparently retried on another available node in the cluster without exposing an error to the client.
3. The client receives a `503 Service Unavailable` (`router_unavailable`) error only if all available nodes in the cluster have been exhausted.

---

## Diagnostics & Support

Every API response (both successful responses and errors) includes a unique tracking identifier.

- **HTTP Headers**: `X-Request-Id` and `X-Correlation-Id`.
- **JSON Error Body**: `error.request_id`.

When contacting customer support, please provide the `request_id`, request timestamp, and model ID — this allows engineers to immediately inspect distributed telemetry logs.