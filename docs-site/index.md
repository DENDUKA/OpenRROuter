---
layout: home

hero:
  name: "OpenRussiaRouter Docs"
  text: "Единый API для LLM"
  tagline: "OpenAI-совместимый шлюз с балансом в USD. Один ключ, один формат запросов."
  actions:
    - theme: brand
      text: Быстрый старт
      link: /
    - theme: alt
      text: Справочник API
      link: /api/reference

features:
  - title: Совместимость с OpenAI
    details: Используйте привычные библиотеки. Формат запросов полностью совместим с OpenAI Chat Completions API.
  - title: Единый баланс
    details: Пополняйте баланс в одном месте и используйте любые доступные модели от разных провайдеров.
  - title: Маршрутизация
    details: Автоматический выбор оптимальной ноды 9Router для обеспечения высокой доступности и минимальной задержки.
---

# Быстрый старт

**OpenRussiaRouter** — это единый API-шлюз для доступа к ведущим языковым моделям.

Все запросы отправляются в формате [OpenAI Chat Completion API](https://platform.openai.com/docs/api-reference/chat). Это значит, что вы можете менять модели, просто изменяя параметр `model` — без переписывания кода.

## Начало работы

1. Зарегистрируйтесь в платформе.
2. Пополните баланс (в USD).
3. Создайте API-ключ в разделе ключей (формат `sk-or-***`).
4. Используйте ключ в любой библиотеке, совместимой с OpenAI.

## Примеры использования

::: code-group

```python [Python]
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-ваш-ключ",
    base_url="https://api.openrussiarouter.com/v1/",
)

response = client.chat.completions.create(
    model="gpt-4o", # Укажите нужную модель
    messages=[{"role": "user", "content": "Привет! Как дела?"}],
)

print(response.choices[0].message.content)
```

```javascript [Node.js]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-ваш-ключ",
  baseURL: "https://api.openrussiarouter.com/v1/",
});

async function main() {
  const response = await client.chat.completions.create({
    model: "gpt-4o",
    messages: [{ role: "user", content: "Привет! Как дела?" }],
  });

  console.log(response.choices[0].message.content);
}

main();
```

```bash [cURL]
curl https://api.openrussiarouter.com/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer sk-or-ваш-ключ" \
  -d '{
    "model": "gpt-4o",
    "messages": [
      {
        "role": "user",
        "content": "Привет! Как дела?"
      }
    ]
  }'
```

:::
