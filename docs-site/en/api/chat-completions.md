# Chat Completions

The main endpoint for interacting with language models.

`POST /v1/chat/completions`

## Request Parameters

| Parameter | Type | Description |
|---|---|---|
| `model` | string | **Required.** Model ID (e.g., `gpt-4o`). |
| `messages` | array | **Required.** Array of conversation messages. Each message contains `role` (user, assistant, system) and `content`. |
| `stream` | boolean | Whether to send the response in chunks as it's generated (Server-Sent Events). |

*The gateway automatically detects the need for streaming by the presence of the `stream: true` flag in the JSON.*

## Basic Request Example

```json
{
  "model": "gpt-4o-mini",
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user", "content": "Tell me a joke." }
  ],
  "stream": false
}
```

## Retry and Failover

The OpenRussiaRouter gateway includes a built-in **failover** mechanism.
If the selected node returns a network error or a status >= 400 **before data transmission begins** (the first byte of the response), the gateway will automatically exclude the problematic node and retry the request on another available node without interrupting your request execution.