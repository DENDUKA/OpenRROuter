# API Overview

OpenRussiaRouter provides an OpenAI-compatible API. All endpoints are available at the base URL:

```
https://api.openrussiarouter.com
```

## Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/v1/chat/completions` | Model response generation (supports streaming). |
| `POST` | `/v1/chat/completions/stream` | Explicit alias for streaming responses. |
| `GET` | `/v1/models` | Retrieve list of available models. |

## Correlation Identifier

Every response from the API includes headers for tracing and debugging:
- `X-Correlation-Id`
- `X-Request-Id`

If you contact support with an issue, please provide the `X-Correlation-Id` value from the problematic response.