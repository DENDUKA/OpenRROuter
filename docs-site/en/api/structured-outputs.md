# Structured Outputs

The `response_format` parameter allows you to guarantee that the model will return a response strictly in JSON format matching a specified JSON Schema.

This eliminates the need to strip markdown wrappers (such as ` ```json `) and avoids JSON syntax errors during parsing.

---

## 1. Basic JSON Mode

A simple mode ensuring the model outputs a valid JSON object.

```json
{
  "model": "gpt-4o-mini",
  "messages": [
    { "role": "system", "content": "You are a helpful assistant that responds strictly in JSON." },
    { "role": "user", "content": "List 3 rainbow colors as an array named colors." }
  ],
  "response_format": { "type": "json_object" }
}
```

::: warning Important Requirement for JSON Mode
When using `"type": "json_object"`, the word `JSON` **must** be present in the system or user prompt, otherwise the API will return a validation error.
:::

---

## 2. Strict Output via JSON Schema

Structured Outputs (`json_schema`) mode guarantees 100% adherence to your specified data schema:

```json
{
  "model": "gpt-4o",
  "messages": [
    { "role": "user", "content": "Generate a user profile for John, 28 years old, software engineer." }
  ],
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "user_profile",
      "strict": true,
      "schema": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "age": { "type": "integer" },
          "occupation": { "type": "string" },
          "skills": {
            "type": "array",
            "items": { "type": "string" }
          }
        },
        "required": ["name", "age", "occupation", "skills"],
        "additionalProperties": false
      }
    }
  }
}
```

---

## Examples

::: code-group

```python [Python (Pydantic + OpenAI)]
from typing import List
from pydantic import BaseModel
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-your-key",
    base_url="https://api.rawrter.com/v1",
)

class ArticleSummary(BaseModel):
    title: str
    tags: List[str]
    word_count_estimate: int
    bullet_points: List[str]

completion = client.beta.chat.completions.parse(
    model="gpt-4o",
    messages=[
        {"role": "system", "content": "Summarize the article."},
        {"role": "user", "content": "Artificial intelligence is changing software development..."},
    ],
    response_format=ArticleSummary,
)

summary: ArticleSummary = completion.choices[0].message.parsed
print(f"Title: {summary.title}")
print(f"Tags: {', '.join(summary.tags)}")
for point in summary.bullet_points:
    print(f"- {point}")
```

```javascript [Node.js (Zod)]
import OpenAI from "openai";
import { zodResponseFormat } from "openai/helpers/zod";
import { z } from "zod";

const client = new OpenAI({
  apiKey: "sk-or-your-key",
  baseURL: "https://api.rawrter.com/v1",
});

const EventInfo = z.object({
  eventName: z.string(),
  date: z.string(),
  participants: z.array(z.string()),
});

async function main() {
  const completion = await client.beta.chat.completions.parse({
    model: "gpt-4o-mini",
    messages: [
      { role: "user", content: "AI Conf is on October 25. Attendees: Alice, Bob." }
    ],
    response_format: zodResponseFormat(EventInfo, "event_info"),
  });

  const event = completion.choices[0].message.parsed;
  console.log("Event:", event?.eventName);
  console.log("Date:", event?.date);
  console.log("Participants:", event?.participants);
}

main();
```

:::
