# Structured Outputs (Структурированный вывод)

Параметр `response_format` позволяет гарантировать, что модель вернет ответ строго в формате JSON, соответствующем заданной JSON Schema.

Это исключает необходимость очистки ответа от markdown-обёрток (таких как ` ```json `) и предотвращает синтаксические ошибки при парсинге.

---

## 1. Базовый JSON Mode

Простой режим, заставляющий модель выдать валидный JSON-объект.

```json
{
  "model": "gpt-4o-mini",
  "messages": [
    { "role": "system", "content": "Ты помощник, который отвечает исключительно в формате JSON." },
    { "role": "user", "content": "Перечисли 3 цвета радуги в виде массива colors." }
  ],
  "response_format": { "type": "json_object" }
}
```

::: warning Важное требование для JSON Mode
При использовании `"type": "json_object"` в системном или пользовательском промпте **обязательно** должно присутствовать слово `JSON`, иначе API вернет ошибку валидации.
:::

---

## 2. Строгий вывод по схеме (JSON Schema)

Режим Structured Outputs (`json_schema`) гарантирует 100% соответствие ответа вашей схеме данных:

```json
{
  "model": "gpt-4o",
  "messages": [
    { "role": "user", "content": "Сгенерируй профиль пользователя: Иван, 28 лет, программист." }
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

## Примеры использования

::: code-group

```python [Python (Pydantic + OpenAI)]
from typing import List
from pydantic import BaseModel
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-ваш-ключ",
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
        {"role": "system", "content": "Сделай выжимку статьи."},
        {"role": "user", "content": "Искусственный интеллект активно меняет разработку ПО..."},
    ],
    response_format=ArticleSummary,
)

summary: ArticleSummary = completion.choices[0].message.parsed
print(f"Заголовок: {summary.title}")
print(f"Теги: {', '.join(summary.tags)}")
for point in summary.bullet_points:
    print(f"- {point}")
```

```javascript [Node.js (Zod)]
import OpenAI from "openai";
import { zodResponseFormat } from "openai/helpers/zod";
import { z } from "zod";

const client = new OpenAI({
  apiKey: "sk-or-ваш-ключ",
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
      { role: "user", content: "Конференция AI Conf пройдет 25 октября. Участвуют: Анна, Петр." }
    ],
    response_format: zodResponseFormat(EventInfo, "event_info"),
  });

  const event = completion.choices[0].message.parsed;
  console.log("Событие:", event?.eventName);
  console.log("Дата:", event?.date);
  console.log("Участники:", event?.participants);
}

main();
```

:::
