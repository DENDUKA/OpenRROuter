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

---

## Streaming Mode (SSE)

For interactive real-time output, set `"stream": true`. The gateway automatically injects `stream_options: {"include_usage": true}` so that the final chunk contains accurate token usage statistics.

::: code-group

```python [Python (Streaming)]
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-your-key",
    base_url="https://api.rawrter.com/v1",
)

stream = client.chat.completions.create(
    model="gpt-4o",
    messages=[{"role": "user", "content": "Write a short poem."}],
    stream=True,
    stream_options={"include_usage": True},
)

for chunk in stream:
    if chunk.choices and chunk.choices[0].delta.content:
        print(chunk.choices[0].delta.content, end="", flush=True)
    
    if chunk.usage:
        print(f"\n\n[Tokens Used: {chunk.usage.total_tokens}]")
```

```javascript [Node.js (Streaming)]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-your-key",
  baseURL: "https://api.rawrter.com/v1",
});

async function main() {
  const stream = await client.chat.completions.create({
    model: "gpt-4o",
    messages: [{ role: "user", content: "Write a short poem." }],
    stream: true,
    stream_options: { include_usage: true },
  });

  for await (const chunk of stream) {
    process.stdout.write(chunk.choices[0]?.delta?.content || "");
    if (chunk.usage) {
      console.log(`\n\n[Tokens Used: ${chunk.usage.total_tokens}]`);
    }
  }
}

main();
```

:::

---

## Retry and Failover

The Rawrter gateway includes a built-in **failover** mechanism.
If the selected node returns a network error or a status >= 400 **before data transmission begins** (the first byte of the response), the gateway will automatically exclude the problematic node and retry the request on another available node without interrupting your request execution.
