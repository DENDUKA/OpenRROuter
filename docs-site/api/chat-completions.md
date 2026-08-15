# Chat Completions

Основной эндпоинт для взаимодействия с языковыми моделями.

`POST /v1/chat/completions`

## Параметры запроса

| Параметр | Тип | Описание |
|---|---|---|
| `model` | string | **Обязательно.** ID модели (например, `gpt-4o`). |
| `messages` | array | **Обязательно.** Массив сообщений диалога. Каждое сообщение содержит `role` (user, assistant, system) и `content`. |
| `stream` | boolean | Отправлять ли ответ частями по мере генерации (Server-Sent Events). |

*Шлюз автоматически определяет необходимость стриминга по наличию флага `stream: true` в JSON.*

## Пример обычного запроса

```json
{
  "model": "gpt-4o-mini",
  "messages": [
    { "role": "system", "content": "Вы — полезный ассистент." },
    { "role": "user", "content": "Расскажи шутку." }
  ],
  "stream": false
}
```

---

## Потоковый режим (Streaming / SSE)

Для интерактивного вывода ответа в реальном времени передайте `"stream": true`. Шлюз автоматически добавит `stream_options: {"include_usage": true}`, чтобы финальный чанк содержал точную статистику использованных токенов.

::: code-group

```python [Python (Streaming)]
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-ваш-ключ",
    base_url="https://api.rawrter.com/v1",
)

stream = client.chat.completions.create(
    model="gpt-4o",
    messages=[{"role": "user", "content": "Напиши короткое стихотворение."}],
    stream=True,
    stream_options={"include_usage": True},
)

for chunk in stream:
    if chunk.choices and chunk.choices[0].delta.content:
        print(chunk.choices[0].delta.content, end="", flush=True)
    
    if chunk.usage:
        print(f"\n\n[Использовано токенов: {chunk.usage.total_tokens}]")
```

```javascript [Node.js (Streaming)]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-ваш-ключ",
  baseURL: "https://api.rawrter.com/v1",
});

async function main() {
  const stream = await client.chat.completions.create({
    model: "gpt-4o",
    messages: [{ role: "user", content: "Напиши короткое стихотворение." }],
    stream: true,
    stream_options: { include_usage: true },
  });

  for await (const chunk of stream) {
    process.stdout.write(chunk.choices[0]?.delta?.content || "");
    if (chunk.usage) {
      console.log(`\n\n[Использовано токенов: ${chunk.usage.total_tokens}]`);
    }
  }
}

main();
```

:::

---

## Retry и Failover

Шлюз Rawrter включает встроенный механизм **failover**.
Если выбранная нода возвращает сетевую ошибку или статус >= 400 **до начала передачи данных** (первого байта ответа), шлюз автоматически исключит проблемную ноду и повторит запрос на другой доступной ноде, не прерывая выполнение вашего запроса.

