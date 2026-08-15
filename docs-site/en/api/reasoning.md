# Reasoning Models

The Rawrter platform supports leading reasoning models, such as **DeepSeek R1**, **OpenAI o1/o3-mini**, **GLM-4-Plus**, and others.

Models in this category generate an internal Chain-of-Thought before producing the final answer, allowing them to excel at complex math, algorithmic tasks, and logical reasoning.

---

## Controlling Reasoning Depth

You can control the volume and depth of internal reasoning using the `reasoning_effort` parameter:

| `reasoning_effort` Value | Description |
|---|---|
| `low` | Minimal reasoning. Fast response with token savings. |
| `medium` | Balanced mode (default for most reasoning models). |
| `high` | Maximum depth of analysis for complex and multi-step tasks. |

The Rawrter gateway automatically verifies reasoning support for the selected model and formats parameters for the upstream provider. If no parameter is specified by the client, the gateway applies the model's curated default `reasoning_effort`.

---

## Parameter Passing Formats

You can pass the parameter as a top-level string `reasoning_effort` or via a `reasoning` object:

::: code-group

```json [Flat parameter]
{
  "model": "deepseek/deepseek-r1",
  "messages": [
    { "role": "user", "content": "Prove that there are infinitely many prime numbers." }
  ],
  "reasoning_effort": "high"
}
```

```json [Reasoning object]
{
  "model": "deepseek/deepseek-r1",
  "messages": [
    { "role": "user", "content": "Prove that there are infinitely many prime numbers." }
  ],
  "reasoning": {
    "effort": "high"
  }
}
```

:::

---

## Accessing Reasoning Thoughts

### Non-streaming Response

The model's thoughts are returned in the dedicated `reasoning_content` field (or `reasoning` object inside `message`), while the final answer is provided in `content`:

```json
{
  "id": "chatcmpl-9xyz...",
  "object": "chat.completion",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Assume for contradiction that the set of prime numbers is finite...",
        "reasoning_content": "Let us begin with Euclid's classical proof by contradiction..."
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 18,
    "completion_tokens": 420,
    "total_tokens": 438
  }
}
```

### Streaming Mode (SSE)

When `stream: true` is enabled, reasoning deltas arrive in the initial SSE chunks via `delta.reasoning_content` before the main text generation begins in `delta.content`:

```text
data: {"choices":[{"index":0,"delta":{"reasoning_content":"Analyzing "}}]}
data: {"choices":[{"index":0,"delta":{"reasoning_content":"the theorem condition..."}}]}
data: {"choices":[{"index":0,"delta":{"content":"Proof:"}}]}
data: {"choices":[{"index":0,"delta":{"content":" Consider..."}}]}
data: [DONE]
```

---

## Usage Examples

::: code-group

```python [Python (OpenAI SDK)]
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-your-key",
    base_url="https://api.rawrter.com/v1",
)

response = client.chat.completions.create(
    model="deepseek/deepseek-r1",
    messages=[
        {"role": "user", "content": "Solve the equation: x^3 - 6x^2 + 11x - 6 = 0"}
    ],
    extra_body={"reasoning_effort": "high"}
)

# Extract thoughts and answer
message = response.choices[0].message
if hasattr(message, "reasoning_content") and message.reasoning_content:
    print("=== Model Thoughts ===")
    print(message.reasoning_content)

print("\n=== Final Answer ===")
print(message.content)
```

```javascript [Node.js]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-your-key",
  baseURL: "https://api.rawrter.com/v1",
});

async function main() {
  const response = await client.chat.completions.create({
    model: "deepseek/deepseek-r1",
    messages: [
      { role: "user", content: "Solve the equation: x^3 - 6x^2 + 11x - 6 = 0" }
    ],
    // @ts-ignore
    reasoning_effort: "high"
  });

  const choice = response.choices[0];
  // @ts-ignore
  if (choice.message.reasoning_content) {
    console.log("=== Model Thoughts ===");
    // @ts-ignore
    console.log(choice.message.reasoning_content);
  }

  console.log("\n=== Final Answer ===");
  console.log(choice.message.content);
}

main();
```

```bash [cURL]
curl https://api.rawrter.com/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer sk-or-your-key" \
  -d '{
    "model": "deepseek/deepseek-r1",
    "messages": [
      {
        "role": "user",
        "content": "Solve the equation: x^3 - 6x^2 + 11x - 6 = 0"
      }
    ],
    "reasoning_effort": "high"
  }'
```

:::
