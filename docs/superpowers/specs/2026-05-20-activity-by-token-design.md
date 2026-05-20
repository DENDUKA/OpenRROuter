# ActivityByToken Design

## Goal

For every request routed to a 9Router node, OpenRussiaRouter must persist token activity by the personal API key that made the request and show that activity in the user workspace.

The activity view must let a user understand usage per personal API key without storing or reading the full bearer token after key creation.

## Current Context

The gateway already validates bearer tokens through AuthService and receives `user_id` plus `api_key_id`. BillingService already records billing usage in `billing.usage_records`, calculates cost, and creates ledger entries. The Workspace Activity page already displays usage-like rows, but it should move to a token-focused activity read model.

9Router responses can expose usage in several compatible shapes. The gateway must normalize these shapes before persistence.

## Data Model

Add a new BillingService table:

```sql
billing.activity_by_token
```

Columns:

- `request_id varchar(128) primary key`
- `api_key_id varchar(64) not null`
- `api_key_prefix varchar(32) not null`
- `model_id varchar(128) not null`
- `router_node_id varchar(64) not null`
- `input_tokens bigint not null`
- `output_tokens bigint not null`
- `cache_tokens bigint not null`
- `status varchar(32) not null`
- `created_at_utc timestamptz not null`
- `completed_at_utc timestamptz not null`

Indexes:

- `(api_key_id, created_at_utc desc)` for Activity queries.
- `(created_at_utc desc)` if admin or diagnostics views need recent global activity later.

Do not store:

- `user_id`: ownership is derived through the personal API key.
- `cost_amount` or `cost_currency`: money stays in billing usage and ledger records.
- full raw bearer tokens.

`api_key_prefix` is a snapshot copied at request time. The UI displays it as `prefix***`.

## Contract Changes

Extend AuthService validation response so Gateway receives the API key prefix:

```proto
message ValidateApiKeyResponse {
  bool is_valid = 1;
  string user_id = 2;
  string api_key_id = 3;
  string error_code = 4;
  string api_key_prefix = 5;
}
```

Extend BillingService usage recording so Gateway sends that prefix:

```proto
message RecordUsageRequest {
  string request_id = 1;
  string user_id = 2;
  string api_key_id = 3;
  string model_id = 4;
  string router_node_id = 5;
  TokenUsage usage = 6;
  string status = 7;
  string started_at_utc = 8;
  string completed_at_utc = 9;
  string api_key_prefix = 10;
}
```

Add a BillingService read endpoint for Activity:

```proto
message ListActivityByTokenRequest {
  repeated string api_key_ids = 1;
}

message ActivityByTokenRecord {
  string request_id = 1;
  string api_key_id = 2;
  string api_key_prefix = 3;
  string model_id = 4;
  string router_node_id = 5;
  TokenUsage usage = 6;
  string status = 7;
  string created_at_utc = 8;
  string completed_at_utc = 9;
}

message ListActivityByTokenResponse {
  repeated ActivityByTokenRecord records = 1;
}
```

## Write Flow

1. Client calls `POST /v1/chat/completions` with a personal bearer token.
2. Gateway validates the token through AuthService.
3. AuthService returns `user_id`, `api_key_id`, and `api_key_prefix`.
4. Gateway checks balance/model, leases a router node, and forwards to 9Router.
5. Gateway extracts normalized token usage from the response. If usage is missing, Gateway uses zero token counts and marks the activity status as `usage_missing`.
6. Gateway sends `RecordUsage` to BillingService with `api_key_id`, `api_key_prefix`, usage, model, router node, timestamps, and activity status.
7. BillingService writes `activity_by_token` for the request.
8. BillingService continues to write `usage_records` and ledger entries for billing.

`activity_by_token` should be idempotent by `request_id`. A repeated `RecordUsage` must not duplicate activity.

## Usage Extraction

Gateway normalizes usage into:

- `input_tokens`
- `output_tokens`
- `cache_tokens`

Supported input token fields:

- `usage.input_tokens`
- `usage.prompt_tokens`

Supported output token fields:

- `usage.output_tokens`
- `usage.completion_tokens`

Supported cache token fields:

- `usage.cache_tokens`
- `usage.cached_tokens`
- `usage.cache_read_input_tokens`
- `usage.prompt_tokens_details.cached_tokens`
- `usage.input_tokens_details.cached_tokens`

For streaming, Gateway must continue requesting `stream_options.include_usage = true` and persist the final usage chunk.

## Read Flow

1. Workspace Activity loads the user's personal API keys from AuthService.
2. WebApp sends the user's `api_key_id` values to BillingService `ListActivityByToken`.
3. BillingService returns activity rows for those keys.
4. Activity page displays each row with:
   - date/time
   - masked token prefix as `prefix***`
   - model
   - input tokens
   - output tokens
   - cache tokens
   - status
   - optional latency computed from timestamps

The Activity page does not need to read or display full bearer tokens.

## Error Handling

If usage extraction fails or all token counts are zero, ActivityByToken still records the request with zero token counts and status `usage_missing`. This preserves the "every request to a node" audit requirement while making missing usage explicit.

If token counts are negative or invalid, BillingService rejects the activity write and records the existing anomaly path used for usage validation.

If billing cost calculation or ledger creation fails after valid activity data is available, ActivityByToken should still preserve the technical activity row when feasible. Billing money failures should stay visible through billing anomalies.

## Tests

Gateway tests:

- Extracts cache tokens from nested OpenAI `prompt_tokens_details.cached_tokens`.
- Extracts cache tokens from Responses-style `input_tokens_details.cached_tokens`.
- Sends `api_key_prefix` in `RecordUsage`.
- Streaming flow preserves final usage and prefix.
- Missing usage still creates an ActivityByToken row with zero token counts and `usage_missing`.

BillingService tests:

- `RecordUsage` writes one `activity_by_token` row.
- Repeated `RecordUsage` with the same `request_id` remains idempotent.
- `ListActivityByToken` returns only requested API key ids.
- Activity rows do not include `user_id` or cost fields.

WebApp tests:

- Activity displays `prefix***`.
- Activity filters to the current user's personal keys.
- Input, output, and cache token columns render from `ActivityByToken`.
