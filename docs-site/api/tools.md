# Function Calling & Tools

Параметр `tools` позволяет подключить внешние функции и сервисы к моделям в Rawrter. Модель не выполняет код самостоятельно — она генерирует структурированные аргументы для вызова ваших локальных функций, а затем формирует финальный ответ на основе полученных результатов.

---

## Основные параметры

| Параметр | Тип | Описание |
|---|---|---|
| `tools` | array | Список доступных инструментов. Каждый элемент имеет тип `function` и содержит описание сигнатуры функции в формате JSON Schema. |
| `tool_choice` | string \| object | Управление выбором инструмента: `"auto"` (по умолчанию), `"none"` (запретить вызовы), `"required"` (обязательно вызвать один из тулов) или `{ "type": "function", "function": { "name": "..." } }`. |

::: tip Автоматическая совместимость со схемами
Шлюз Rawrter автоматически инлайнит рекурсивные и ссылочные схемы (`$ref`, `$defs`) при маршрутизации запросов к моделям, которые не поддерживают внешние ссылки в JSON Schema (например, линейка Google Gemini).
:::

---

## Порядок выполнения (Workflow)

1. **Запрос:** Клиент отправляет сообщение пользователя и массив `tools`.
2. **Ответ модели:** Модель возвращает сообщение с `finish_reason: "tool_calls"` и массивом `tool_calls` с аргументами в формате JSON.
3. **Локальное выполнение:** Ваше приложение выполняет вызванную функцию.
4. **Финальный ответ:** Клиент отправляет результат функции в историю сообщений с ролью `role: "tool"` и соответствующим `tool_call_id`. Модель генерирует финальный ответ пользователю.

---

## Пример на Python

::: code-group

```python [Python (OpenAI SDK)]
import json
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-ваш-ключ",
    base_url="https://api.rawrter.com/v1",
)

# 1. Описание инструмента
tools = [
    {
        "type": "function",
        "function": {
            "name": "get_current_weather",
            "description": "Получить текущую погоду в заданном городе",
            "parameters": {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "string",
                        "description": "Город, например, Москва, Париж",
                    },
                    "unit": {"type": "string", "enum": ["celsius", "fahrenheit"]},
                },
                "required": ["location"],
            },
        },
    }
]

messages = [{"role": "user", "content": "Какая погода в Москве?"}]

# 2. Первый запрос: модель решает вызвать инструмент
response = client.chat.completions.create(
    model="gpt-4o",
    messages=messages,
    tools=tools,
    tool_choice="auto",
)

response_message = response.choices[0].message
messages.append(response_message)

# 3. Обработка tool_calls
if response_message.tool_calls:
    for tool_call in response_message.tool_calls:
        function_name = tool_call.function.name
        function_args = json.loads(tool_call.function.arguments)
        
        # Симуляция выполнения функции
        if function_name == "get_current_weather":
            function_response = json.dumps({
                "location": function_args.get("location"),
                "temperature": "+18",
                "condition": "Солнечно"
            })
            
            # 4. Передача результата функции обратно в модель
            messages.append({
                "tool_call_id": tool_call.id,
                "role": "tool",
                "name": function_name,
                "content": function_response,
            })

    # Финальный ответ модели
    final_response = client.chat.completions.create(
        model="gpt-4o",
        messages=messages,
    )
    print(final_response.choices[0].message.content)
```

```javascript [Node.js]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-ваш-ключ",
  baseURL: "https://api.rawrter.com/v1",
});

const tools = [
  {
    type: "function",
    function: {
      name: "get_current_weather",
      description: "Получить погоду в городе",
      parameters: {
        type: "object",
        properties: {
          location: { type: "string" },
        },
        required: ["location"],
      },
    },
  },
];

async function main() {
  const messages = [{ role: "user", content: "Какая погода в Санкт-Петербурге?" }];

  const response = await client.chat.completions.create({
    model: "gpt-4o",
    messages,
    tools,
  });

  const message = response.choices[0].message;
  messages.push(message);

  if (message.tool_calls) {
    for (const toolCall of message.tool_calls) {
      if (toolCall.function.name === "get_current_weather") {
        const result = JSON.stringify({ location: "Санкт-Петербург", temperature: "+15°C" });
        messages.push({
          role: "tool",
          tool_call_id: toolCall.id,
          content: result,
        });
      }
    }

    const finalResponse = await client.chat.completions.create({
      model: "gpt-4o",
      messages,
    });

    console.log(finalResponse.choices[0].message.content);
  }
}

main();
```

:::
