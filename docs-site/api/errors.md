---
title: "Обработка ошибок API и коды статусов | Rawrter Docs"
description: "Полное руководство разработчика по обработке ошибок API Rawrter: форматы JSON, коды платформы, retry-стратегии, exponential backoff и примеры на Python, TypeScript и cURL."
---

# Обработка ошибок API

API Rawrter полностью совместим с форматом [OpenAI API Error Response](https://platform.openai.com/docs/guides/error-codes). В случае ошибки сервер возвращает стандартный JSON-ответ и соответствующий HTTP-код статуса (4xx или 5xx).

Каждый ответ об ошибке содержит детальную информацию о причине сбоя, машиночитаемый код и уникальный идентификатор запроса (`request_id`) для трассировки и обращения в поддержку.

---

## Формат ответа об ошибке

Ответ об ошибке всегда возвращается в виде JSON-объекта верхнего уровня с полем `error`:

```json
{
  "error": {
    "message": "Недостаточно средств на балансе для выполнения запроса.",
    "type": "billing_error",
    "code": "insufficient_balance",
    "param": null,
    "request_id": "0HN123456789A:00000001"
  }
}
```

### Поля объекта `error`

| Поле | Тип | Описание |
|---|---|---|
| `message` | `string` | Человекочитаемое описание возникшей ошибки на русском или английском языке. |
| `type` | `string` | Категория ошибки (например, `invalid_request_error`, `authentication_error`, `billing_error`, `rate_limit_error`, `service_unavailable`, `internal_error`). |
| `code` | `string \| null` | Машиночитаемый код ошибки платформы или upstream-провайдера (например, `insufficient_balance`, `invalid_api_key`, `model_unavailable`). |
| `param` | `string \| null` | Имя параметра запроса, вызвавшего ошибку (если применимо, например, `model`, `messages`, `temperature`). |
| `request_id` | `string \| null` | Уникальный Correlation ID запроса. Дублируется в заголовках ответа `X-Request-Id` и `X-Correlation-Id`. |

---

## Коды HTTP-статусов и платформы

В таблице ниже приведены все стандартные HTTP-статусы, возвращаемые шлюзом Rawrter, соответствующие им коды платформы, причины возникновения и рекомендуемые действия на стороне клиента.

| HTTP статус | Код ошибки (`code`) | Тип ошибки (`type`) | Причина возникновения | Рекомендуемое действие клиента |
|---|---|---|---|---|
| **400** Bad Request | `validation_failed` | `invalid_request_error` | Невалидный JSON, отсутствуют обязательные поля (`model`, `messages`), переданы некорректные типы данных или конфликтующие параметры. | Проверить синтаксис тела запроса и соответствие схемы параметров. **Не повторять запрос без исправлений.** |
| **401** Unauthorized | `authentication_required` | `authentication_error` | В запросе отсутствует заголовок `Authorization` с Bearer-токеном. | Добавить заголовок `Authorization: Bearer sk-or-...` с действующим ключом. |
| **401** Unauthorized | `invalid_api_key` | `authentication_error` | Передан недействительный, удаленный, заблокированный или отозванный API-ключ. | Проверить корректность API-ключа в [панели управления](https://rawrter.com/ru) и обновить конфигурацию приложения. |
| **402** Payment Required | `insufficient_balance` | `billing_error` | Баланс аккаунта нулевой или отрицательный, либо на счете недостаточно средств для запуска генерации на выбранной платной модели. | Пополнить баланс аккаунта в личном кабинете Rawrter. |
| **402** Payment Required | `api_key_limit_exceeded` | `billing_error` | Превышен установленный лимит расходов (spend limit) для данного конкретного API-ключа. | Увеличить лимит расходов ключа в панели управления или выпустить новый ключ. |
| **403** Forbidden | `access_denied` / `forbidden` | `permission_error` | Доступ к запрошенной модели или функции ограничен политиками доступа аккаунта. | Проверить права доступа и настройки аккаунта в личном кабинете. |
| **404** Not Found | `model_unavailable` | `invalid_request_error` | Запрошенная модель (`model`) не существует, отключена или в URL указан неверный эндпоинт. | Сверить ID модели со списком доступных моделей через `GET /v1/models` или в [каталоге моделей](/guide/models). |
| **429** Too Many Requests | `rate_limited` | `rate_limit_error` | Превышен лимит частоты запросов (RPM/RPS) для API-ключа или IP-адреса. | Приостановить отправку запросов на время из заголовка `Retry-After` (в секундах) и настроить экспоненциальный backoff. |
| **500** Internal Server Error | `internal_error` | `internal_error` | Непредвиденный сбой на стороне шлюза Rawrter при обработке запроса. | Повторить запрос с экспоненциальной задержкой (retry). Если ошибка повторяется, обратиться в поддержку с `request_id`. |
| **502** Bad Gateway | `bad_gateway` | `service_unavailable` | Ошибка соединения с вышестоящим провайдером или получение некорректного ответа от ноды. | Повторить запрос через 1–2 секунды (шлюз обычно выполняет автоматический failover на другую ноду). |
| **503** Service Unavailable | `router_unavailable` | `service_unavailable` | В данный момент нет свободных нод или маршрутизаторов для обработки выбранной модели. | Повторить запрос с экспоненциальной задержкой (Exponential Backoff). |

---

## Стратегия повторных запросов (Retry) и Backoff

При разработке надежных интеграций с LLM API критически важно разделять ошибки на **повторяемые** (transient) и **неповторяемые** (permanent).

### Повторяемые и неповторяемые ошибки

- **Неповторяемые ошибки (400, 401, 402, 403, 404)**:  
  Повторная отправка идентичного запроса гарантированно завершится той же ошибкой. Повторять запрос программно **запрещено** — требуется исправление кода, ключа, пополнение баланса или изменение ID модели.
- **Повторяемые ошибки (429, 500, 502, 503, 504)**:  
  Вызваны временными факторами (кратковременная пиковая нагрузка, сетевой сбой, недоступность ноды). Такие запросы следует повторять с использованием **экспоненциальной задержки** (Exponential Backoff) и случайного отклонения (Jitter).

### Обработка заголовка `Retry-After` при коде 429

Когда шлюз возвращает статус `429 Too Many Requests`, в HTTP-заголовках ответа передается заголовок `Retry-After`:

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 5
X-Request-Id: 0HN123456789A:00000001
```

Клиент должен ожидать указанное в заголовке количество секунд перед повторной отправкой запроса. Если заголовок отсутствует, используйте формулу экспоненциального бэкоффа.

### Формула экспоненциального бэкоффа с Jitter

Для предотвращения эффекта «набегающей толпы» (thundering herd problem) добавляйте случайный шум (джиттер):

$$\text{delay} = \min(\text{max\_delay}, \text{base\_delay} \times 2^{\text{attempt}}) + \text{jitter}$$

- $\text{base\_delay}$ — начальная задержка (например, 1.0 сек)
- $\text{attempt}$ — номер текущей попытки (0, 1, 2...)
- $\text{max\_delay}$ — максимальный предел паузы (например, 30–60 сек)
- $\text{jitter}$ — случайное число от 0 до 500 мс

Рекомендуемое количество попыток: **3–5 раз**.

---

## Практические примеры обработки ошибок

Ниже приведены готовые примеры надежной обработки ошибок и повторных попыток для популярных языков и инструментов.

::: code-group

```python [Python (OpenAI SDK)]
import time
import random
from openai import (
    OpenAI,
    APIError,
    AuthenticationError,
    RateLimitError,
    BadRequestError,
    APIConnectionError,
    InternalServerError,
)

client = OpenAI(
    api_key="sk-or-ваш-ключ",
    base_url="https://api.rawrter.com/v1",
)

def generate_chat_completion(messages, model="gpt-4o", max_retries=3):
    """
    Выполняет запрос Chat Completion с перехватом типизированных ошибок
    и экспоненциальным backoff для повторяемых сбоев (429, 5xx, Network).
    """
    for attempt in range(max_retries):
        try:
            response = client.chat.completions.create(
                model=model,
                messages=messages,
            )
            return response.choices[0].message.content

        except AuthenticationError as e:
            # 401: Неверный API-ключ или отсутствие заголовка Authorization
            print(f"[401] Ошибка аутентификации: {e.message}")
            print("Проверьте правильность API-ключа sk-or-*** в личном кабинете.")
            raise

        except BadRequestError as e:
            # 400 / 404: Невалидные параметры, неверная модель
            print(f"[400/404] Ошибка в запросе: {e.message} (код: {e.code}, param: {e.param})")
            raise

        except RateLimitError as e:
            # 429: Превышен лимит запросов
            retry_after_header = getattr(e.response, "headers", {}).get("retry-after")
            if retry_after_header:
                delay = float(retry_after_header)
            else:
                delay = (2 ** attempt) + random.uniform(0.1, 0.5)

            print(f"[429] Лимит запросов превышен. Повтор через {delay:.2f} сек (попытка {attempt + 1}/{max_retries})...")
            time.sleep(delay)

        except APIConnectionError as e:
            # Сетевой сбой или обрыв соединения
            delay = (2 ** attempt) + random.uniform(0.1, 0.5)
            print(f"[Сетевая ошибка] {e}. Повтор через {delay:.2f} сек...")
            time.sleep(delay)

        except InternalServerError as e:
            # 500 / 502 / 503: Серверная ошибка
            delay = (2 ** attempt) + random.uniform(0.1, 0.5)
            print(f"[{e.status_code}] Сбой сервера: {e.message}. Повтор через {delay:.2f} сек...")
            time.sleep(delay)

        except APIError as e:
            # Прочие ошибки API (например, 402 Insufficient Balance)
            print(f"[{e.status_code}] Ошибка API: {e.message} (код: {e.code})")
            if e.status_code and e.status_code in (429, 500, 502, 503):
                delay = (2 ** attempt) + random.uniform(0.1, 0.5)
                time.sleep(delay)
            else:
                raise

    raise RuntimeError(f"Не удалось выполнить запрос после {max_retries} попыток.")

# Пример вызова
if __name__ == "__main__":
    try:
        reply = generate_chat_completion([{"role": "user", "content": "Привет, Rawrter!"}])
        print("Ответ модели:", reply)
    except Exception as err:
        print("Запрос завершился с фатальной ошибкой:", err)
```

```typescript [TypeScript / Node.js]
import OpenAI, {
  APIError,
  AuthenticationError,
  BadRequestError,
  RateLimitError,
  InternalServerError,
  APIConnectionError,
} from 'openai';

const client = new OpenAI({
  apiKey: process.env.RAWRTER_API_KEY || 'sk-or-ваш-ключ',
  baseURL: 'https://api.rawrter.com/v1',
});

interface ChatOptions {
  model?: string;
  maxRetries?: number;
}

async function createChatCompletionWithRetry(
  messages: OpenAI.Chat.ChatCompletionMessageParam[],
  options: ChatOptions = {}
): Promise<string> {
  const { model = 'gpt-4o', maxRetries = 3 } = options;

  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      const response = await client.chat.completions.create({
        model,
        messages,
      });

      return response.choices[0]?.message?.content || '';
    } catch (error: any) {
      if (error instanceof AuthenticationError) {
        console.error(`[401] Неверный API-ключ: ${error.message}`);
        throw error; // Неповторяемая ошибка
      }

      if (error instanceof BadRequestError) {
        console.error(`[400] Некорректный запрос: ${error.message} (параметр: ${error.param}, код: ${error.code})`);
        throw error; // Неповторяемая ошибка
      }

      if (error instanceof RateLimitError) {
        const retryAfter = error.headers?.['retry-after'];
        const delaySeconds = retryAfter ? parseFloat(retryAfter) : Math.pow(2, attempt) + Math.random() * 0.5;
        console.warn(`[429] Превышен rate limit. Ожидание ${delaySeconds.toFixed(1)}с перед повтором...`);
        await new Promise((resolve) => setTimeout(resolve, delaySeconds * 1000));
        continue;
      }

      if (error instanceof APIConnectionError) {
        const delayMs = Math.pow(2, attempt) * 1000 + Math.random() * 500;
        console.warn(`[Сетевой сбой] ${error.message}. Повтор через ${Math.round(delayMs)}мс...`);
        await new Promise((resolve) => setTimeout(resolve, delayMs));
        continue;
      }

      if (error instanceof InternalServerError || (error instanceof APIError && (error.status === 502 || error.status === 503))) {
        const delayMs = Math.pow(2, attempt) * 1000 + Math.random() * 500;
        const reqId = error.headers?.['x-request-id'] || 'N/A';
        console.warn(`[${error.status}] Ошибка сервера (Request ID: ${reqId}). Попытка ${attempt + 1}/${maxRetries} через ${Math.round(delayMs)}мс...`);
        await new Promise((resolve) => setTimeout(resolve, delayMs));
        continue;
      }

      if (error instanceof APIError) {
        console.error(`[${error.status}] Ошибка API [${error.code}]: ${error.message}`);
        throw error;
      }

      throw error;
    }
  }

  throw new Error(`Запрос не удался после ${maxRetries} попыток.`);
}
```

```bash [cURL & Bash]
#!/usr/bin/env bash
set -e

API_KEY="sk-or-ваш-ключ"
ENDPOINT="https://api.rawrter.com/v1/chat/completions"

# Выполняем запрос с получением HTTP-статуса и тела ответа
RAW_RESPONSE=$(curl -s -w "\n%{http_code}" "$ENDPOINT" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d '{
    "model": "gpt-4o",
    "messages": [
      { "role": "user", "content": "Привет!" }
    ]
  }')

# Разделяем тело ответа и HTTP код
HTTP_BODY=$(echo "$RAW_RESPONSE" | sed '$d')
HTTP_CODE=$(echo "$RAW_RESPONSE" | tail -n1)

if [ "$HTTP_CODE" -ge 400 ]; then
  echo "❌ Ошибка API (HTTP $HTTP_CODE):"
  
  # Парсинг JSON-ответа с помощью jq
  ERROR_TYPE=$(echo "$HTTP_BODY" | jq -r '.error.type // "unknown"')
  ERROR_CODE=$(echo "$HTTP_BODY" | jq -r '.error.code // "unknown"')
  ERROR_MSG=$(echo "$HTTP_BODY" | jq -r '.error.message // "Нет описания"')
  REQ_ID=$(echo "$HTTP_BODY" | jq -r '.error.request_id // "N/A"')

  echo "  Тип ошибки:  $ERROR_TYPE"
  echo "  Код ошибки:  $ERROR_CODE"
  echo "  Сообщение:   $ERROR_MSG"
  echo "  Request ID:  $REQ_ID"
  exit 1
else
  echo "✅ Успешный ответ (HTTP $HTTP_CODE):"
  echo "$HTTP_BODY" | jq -r '.choices[0].message.content'
fi
```

:::

---

## Ошибки Upstream-провайдеров и Failover

Шлюз Rawrter выполняет маршрутизацию запросов на распределенные ноды и официальные API оригинальных поставщиков моделей (OpenAI, Anthropic, Google, xAI и др.).

### Прозрачное проксирование ошибок

Если запрос успешно прошел авторизацию и маршрутизацию шлюзом, но завершился с ошибкой со стороны самой языковой модели или апстрим-провайдера (например, превышение длины контекста `context_length_exceeded` или срабатывание фильтра безопасности `content_filter`), шлюз возвращает ошибку клиенту в оригинальном формате и с оригинальным HTTP-кодом провайдера.

Это гарантирует 100% обратную совместимость: ваши существующие обработчики исключений в SDK продолжат работать без каких-либо изменений.

### Встроенный механизм Failover

Для обеспечения высокой доступности шлюз Rawrter оснащен автоматическим механизмом **Failover**:

1. Если выбранная нода возвращает сетевую ошибку (соединение разорвано, timeout) или HTTP-статус $\ge 500$ **до начала передачи данных** (первого байта ответа клиенту), шлюз автоматически исключает сбойную ноду.
2. Запрос прозрачно перенаправляется на следующую доступную ноду в кластере без возврата ошибки клиенту.
3. Клиент получает ошибку 503 (`router_unavailable`) только в том случае, если все доступные ноды кластера исчерпаны или недоступны.

---

## Диагностика и обращение в поддержку

Каждый ответ API (как успешный, так и с ошибкой) сопровождается уникальным идентификатором запроса.

- **В заголовках HTTP**: `X-Request-Id` и `X-Correlation-Id`.
- **В теле ошибки JSON**: поле `error.request_id`.

При обращении в техническую поддержку обязательно указывайте `request_id`, время отправки запроса и вызываемую модель — это позволит инженерам моментально локализовать запись в распределенных логах.

