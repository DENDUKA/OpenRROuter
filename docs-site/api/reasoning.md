# Reasoning (Модели мышления)

Платформа Rawrter поддерживает передовые модели рассуждений (reasoning models), такие как **DeepSeek R1**, **OpenAI o1/o3-mini**, **GLM-4-Plus** и другие.

Модели этого класса генерируют внутреннюю цепочку мыслей (Chain-of-Thought) перед выдачей окончательного ответа, что позволяет им эффективно решать сложные математические, алгоритмические и логические задачи.

---

## Управление глубиной рассуждений

Вы можете контролировать объем и глубину внутренних рассуждений с помощью параметра `reasoning_effort`:

| Значение `reasoning_effort` | Описание |
|---|---|
| `low` | Минимальные рассуждения. Быстрый ответ с экономией токенов. |
| `medium` | Сбалансированный режим (дефолтный для большинства моделей). |
| `high` | Максимальная глубина анализа для сложных и многошаговых задач. |

Шлюз Rawrter автоматически проверяет поддержку reasoning выбранной моделью и передает параметры провайдеру в требуемом формате. Если параметр не указан клиентом, шлюз автоматически применит оптимальный дефолтный `reasoning_effort` для модели.

---

## Форматы передачи параметров

Вы можете передать параметр как плоскую строку `reasoning_effort`, так и через объект `reasoning`:

::: code-group

```json [Плоский параметр]
{
  "model": "deepseek/deepseek-r1",
  "messages": [
    { "role": "user", "content": "Докажи, что простых чисел бесконечно много." }
  ],
  "reasoning_effort": "high"
}
```

```json [Объект reasoning]
{
  "model": "deepseek/deepseek-r1",
  "messages": [
    { "role": "user", "content": "Докажи, что простых чисел бесконечно много." }
  ],
  "reasoning": {
    "effort": "high"
  }
}
```

:::

---

## Получение цепочки мыслей

### Обычный ответ (Non-streaming)

В ответе модели текст рассуждений возвращается в специальном поле `reasoning_content` (или объекте `reasoning` внутри `message`), а финальный результат — в поле `content`:

```json
{
  "id": "chatcmpl-9xyz...",
  "object": "chat.completion",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Предположим противное: пусть множество простых чисел конечно...",
        "reasoning_content": "Начнем с классического доказательства Евклида от противного..."
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

### Потоковый режим (Streaming / SSE)

При включенном `stream: true` дельты рассуждений приходят в первых чанках в свойстве `delta.reasoning_content` до того, как начнется генерация основного текста ответа в `delta.content`:

```text
data: {"choices":[{"index":0,"delta":{"reasoning_content":"Анализируем "}}]}
data: {"choices":[{"index":0,"delta":{"reasoning_content":"условие теоремы..."}}]}
data: {"choices":[{"index":0,"delta":{"content":"Доказательство:"}}]}
data: {"choices":[{"index":0,"delta":{"content":" Рассмотрим..."}}]}
data: [DONE]
```

---

## Примеры использования

::: code-group

```python [Python (OpenAI SDK)]
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-ваш-ключ",
    base_url="https://api.rawrter.com/v1",
)

response = client.chat.completions.create(
    model="deepseek/deepseek-r1",
    messages=[
        {"role": "user", "content": "Реши уравнение: x^3 - 6x^2 + 11x - 6 = 0"}
    ],
    extra_body={"reasoning_effort": "high"}
)

# Извлечение мыслей и ответа
message = response.choices[0].message
if hasattr(message, "reasoning_content") and message.reasoning_content:
    print("=== Мысли модели ===")
    print(message.reasoning_content)

print("\n=== Ответ ===")
print(message.content)
```

```javascript [Node.js]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-ваш-ключ",
  baseURL: "https://api.rawrter.com/v1",
});

async function main() {
  const response = await client.chat.completions.create({
    model: "deepseek/deepseek-r1",
    messages: [
      { role: "user", content: "Реши уравнение: x^3 - 6x^2 + 11x - 6 = 0" }
    ],
    // @ts-ignore
    reasoning_effort: "high"
  });

  const choice = response.choices[0];
  // @ts-ignore
  if (choice.message.reasoning_content) {
    console.log("=== Мысли модели ===");
    // @ts-ignore
    console.log(choice.message.reasoning_content);
  }

  console.log("\n=== Ответ ===");
  console.log(choice.message.content);
}

main();
```

```bash [cURL]
curl https://api.rawrter.com/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer sk-or-ваш-ключ" \
  -d '{
    "model": "deepseek/deepseek-r1",
    "messages": [
      {
        "role": "user",
        "content": "Реши уравнение: x^3 - 6x^2 + 11x - 6 = 0"
      }
    ],
    "reasoning_effort": "high"
  }'
```

:::
