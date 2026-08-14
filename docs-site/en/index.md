---
layout: doc
---

# Quick Start

**Rawrter** is a unified API gateway for accessing top language models.

All requests are sent in the [OpenAI Chat Completion API](https://platform.openai.com/docs/api-reference/chat) format. This means you can switch models just by changing the `model` parameter — without rewriting code.

## Getting Started

1. Register on the platform.
2. Top up your balance (in USD).
3. Create an API key in the keys section (format `sk-or-***`).
4. Use the key in any OpenAI-compatible library.

## Usage Examples

::: code-group

```python [Python]
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-your-key",
    base_url="https://api.rawrter.com/v1/",
)

response = client.chat.completions.create(
    model="gpt-4o", # Specify the model
    messages=[{"role": "user", "content": "Hello! How are you?"}],
)

print(response.choices[0].message.content)
```

```javascript [Node.js]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-your-key",
  baseURL: "https://api.rawrter.com/v1/",
});

async function main() {
  const response = await client.chat.completions.create({
    model: "gpt-4o",
    messages: [{ role: "user", content: "Hello! How are you?" }],
  });

  console.log(response.choices[0].message.content);
}

main();
```

```bash [cURL]
curl https://api.rawrter.com/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer sk-or-your-key" \
  -d '{
    "model": "gpt-4o",
    "messages": [
      {
        "role": "user",
        "content": "Hello! How are you?"
      }
    ]
  }'
```

:::
