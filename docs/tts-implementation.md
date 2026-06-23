# TTS (Text-to-Speech) Implementation

> Документация по реализации TTS в OpenRussiaRouter.

## Обзор

Добавлен OpenAI-совместимый endpoint `POST /v1/audio/speech` для генерации речи из текста. Поддерживается полная спецификация OpenAI TTS API с дополнительными параметрами MiniMax.

## Архитектура

```
Клиент (OpenAI SDK / curl / приложение)
    ↓
OpenRussiaRouter ApiGateway (порт 5000)
    ↓
POST /v1/audio/speech
    ↓
TextToSpeechHandler
    ↓
┌─────────────────────────────────────────────┐
│ 1. Валидация: model, input, voice          │
│ 2. Авторизация: AuthService                │
│ 3. Проверка баланса: BillingService        │
│ 4. Выбор ноды: ControlPlane                │
│ 5. Патч JSON: model → routerModelId        │
│ 6. Проксирование → 9Router                 │
│ 7. Биллинг: input.Length = output_tokens   │
│ 8. Ответ клиенту (бинарный аудио / JSON)   │
└─────────────────────────────────────────────┘
    ↓
9Router (порт 20127)
    ↓
MiniMax API (https://api.minimax.io/v1/t2a_v2)
```

## Endpoint

### `POST /v1/audio/speech`

Генерирует аудио из текста. Полная совместимость с OpenAI TTS API.

#### Заголовки

| Заголовок | Обязательный | Описание |
|---|---|---|
| `Authorization` | Да | `Bearer <api-key>` (формат `sk-or-*`) |
| `Content-Type` | Да | `application/json` |
| `X-Correlation-Id` | Нет | ID для трассировки |

#### Тело запроса

| Параметр | Тип | Обязательный | Дефолт | Описание |
|---|---|---|---|---|
| `model` | string | **Да** | — | ID модели TTS (e.g., `speech-2.8-hd`) |
| `input` | string | **Да** | — | Текст для озвучки (до 4096 символов) |
| `voice` | string | **Да** | — | ID голоса |
| `response_format` | string | Нет | `mp3` | Формат: `mp3`, `opus`, `aac`, `flac`, `wav`, `pcm` |
| `speed` | number | Нет | `1.0` | Скорость 0.25–4.0 |
| `volume` | number | Нет | `1.0` | Громкость |
| `pitch` | number | Нет | `0` | Высота тона |
| `emotion` | string | Нет | — | Эмоция: `happy`, `calm` и т.д. |
| `sample_rate` | number | Нет | `32000` | Частота дискретизации |
| `bitrate` | number | Нет | `128000` | Битрейт |
| `channel` | number | Нет | `1` | Канал (1=mono, 2=stereo) |
| `format` | string | Нет | `mp3` | Внутренний формат аудио |
| `language_boost` | string | Нет | `auto` | Усиление языка |
| `english_normalization` | boolean | Нет | — | Нормализация английского |
| `pronunciation_dict` | object | Нет | — | Кастомное произношение |

#### Query параметры

| Параметр | Описание |
|---|---|
| `?response_format=json` | Вернуть JSON `{audio: base64, format}` вместо бинарного аудио |

#### Ответ

**Бинарный (по умолчанию):**
```
HTTP/1.1 200 OK
Content-Type: audio/mpeg
Content-Length: <bytes>

[бинарный аудио-поток]
```

**JSON (`?response_format=json`):**
```json
HTTP/1.1 200 OK
Content-Type: application/json

{
  "audio": "<base64-encoded-audio>",
  "format": "mp3"
}
```

**Заголовки ответа:**
| Заголовок | Описание |
|---|---|
| `X-Correlation-Id` | ID запроса для трассировки |
| `X-Request-Id` | ID запроса |

## Примеры вызовов

### Минимальный

```bash
curl -X POST http://localhost:5000/v1/audio/speech \
  -H "Authorization: Bearer sk-or-your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "speech-2.8-hd",
    "input": "Привет, мир!",
    "voice": "English_expressive_narrator"
  }' \
  --output hello.mp3
```

### Со всеми параметрами

```bash
curl -X POST http://localhost:5000/v1/audio/speech \
  -H "Authorization: Bearer sk-or-your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "speech-2.8-hd",
    "input": "Привет, это тест TTS API",
    "voice": "English_expressive_narrator",
    "speed": 1.2,
    "volume": 1.0,
    "pitch": 0,
    "format": "mp3",
    "language_boost": "auto"
  }' \
  --output test.mp3
```

### JSON ответ

```bash
curl -X POST "http://localhost:5000/v1/audio/speech?response_format=json" \
  -H "Authorization: Bearer sk-or-your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "speech-2.8-hd",
    "input": "Тест",
    "voice": "English_expressive_narrator"
  }'
```

### Python (OpenAI SDK)

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:5000/v1",
    api_key="sk-or-your-api-key"
)

response = client.audio.speech.create(
    model="speech-2.8-hd",
    input="Привет, это тест",
    voice="English_expressive_narrator",
    speed=1.2,
    response_format="mp3"
)

response.stream_to_file("output.mp3")
```

## Биллинг

### Принцип

TTS не имеет токенов в ответе. Биллинг считает **количество символов Unicode** входного текста как `output_tokens`.

### Формула

```
cost = (characters / 1,000,000) × output_per_1m_usd
```

### Пример расчёта

| Параметр | Значение |
|---|---|
| Входной текст | `Привет, это тест` |
| Символов (Unicode) | 16 |
| Модель | speech-2.8-hd |
| output_per_1m_usd | $48.61 |
| **Стоимость** | 16 × $48.61 / 1,000,000 = **$0.000778** |

### Цены MiniMax TTS

| Модель | input_per_1m_usd | output_per_1m_usd | Цена за 1K символов |
|---|---|---|---|
| speech-2.8-hd | $0 | $48.61 | ~$0.049 |
| speech-2.8-turbo | $0 | $27.78 | ~$0.028 |

### Запись usage

Usage записывается fire-and-forget через `IBillingProvider.RecordUsageAsync`:

```csharp
var characters = (long)input.Length;
var usage = new TokenUsage(InputTokens: 0, OutputTokens: characters, CacheTokens: 0);
var status = characters > 0 ? "completed" : "usage_missing";
RecordUsageFireAndForget(requestId, auth, model, providerName, providerAccount, usage, status, startedAt, completedAt);
```

## Изменённые файлы

### OpenRussiaRouter

| Файл | Изменение |
|---|---|
| `src/OpenRussiaRouter.ApiGateway/Router/IRouterProxyProvider.cs` | Добавлен `SendTextToSpeechAsync` |
| `src/OpenRussiaRouter.ApiGateway/Router/YarpRouterProxyProvider.cs` | Реализация + константа `AudioSpeechPath` |
| `src/OpenRussiaRouter.ApiGateway/Application/Endpoints/TextToSpeechHandler.cs` | **Новый** — полный хендлер |
| `src/OpenRussiaRouter.ApiGateway/Application/Extensions/ApiGatewayExtensions.cs` | DI + endpoint registration |
| `src/OpenRussiaRouter.ApiGateway/appsettings.json` | s2s токен для BillingService |

### 9Router

| Файл | Изменение |
|---|---|
| `open-sse/handlers/ttsProviders/minimax.js` | Принимает все параметры MiniMax API с дефолтами |
| `open-sse/handlers/ttsProviders/genericFormats.js` | Пробрасывает `params` в handler |
| `open-sse/handlers/ttsCore.js` | Пробрасывает `params` через цепочку |
| `src/sse/handlers/tts.js` | Извлекает все параметры из body |

## Логика TextToSpeechHandler

### Поток обработки

```
1. Читаем JSON body
2. Валидация:
   - model (required)
   - input (required)
   - voice (required)
3. Подсчёт символов: characters = input.Length
4. Цикл retry (MaxRouterAttempts = 5):
   a. pipeline.AuthorizeAndAcquireLeaseAsync(model)
      - AuthService: аутентификация API ключа
      - BillingService: проверка баланса
      - ControlPlane: выбор ноды 9Router
   b. PatchModelAndVoice(json, routerModelId, voice)
      - Заменяем model на routerModelId
      - Заменяем voice на переданный клиентом
   c. routerProxy.SendTextToSpeechAsync(...)
      - YARP forward на /v1/audio/speech
   d. ReleaseRouterNodeAsync(completed/failed)
   e. При ошибке (>=400):
      - Нода добавляется в excludedNodes
      - Retry на другой ноде
5. Биллинг:
   - TokenUsage(InputTokens: 0, OutputTokens: characters, CacheTokens: 0)
   - RecordUsageFireAndForget(...)
6. Ответ клиенту:
   - Копируем StatusCode + ContentType + Body от 9Router
```

### Патч JSON

Метод `PatchModelAndVoice` трансформирует JSON:

**Вход:**
```json
{
  "model": "speech-2.8-hd",
  "input": "Привет",
  "voice": "English_expressive_narrator",
  "speed": 1.2
}
```

**Выход (для 9Router):**
```json
{
  "model": "speech-2.8-hd",
  "input": "Привет",
  "voice": "English_expressive_narrator",
  "speed": 1.2
}
```

Примечание: `model` заменяется на `routerModelId` из BillingService.

## Голоса MiniMax

### Встроенные голоса

| Voice ID | Описание |
|---|---|
| `English_expressive_narrator` | Выразительный рассказчик (дефолт) |
| `Wise_Woman` | Мудрая женщина |
| `Friendly_Person` | Дружелюбный человек |
| `Inspirational_girl` | Вдохновляющая девушка |
| `Deep_Voice_Man` | Мужчина с глубоким голосом |
| `Calm_Woman` | Спокойная женщина |
| `Casual_Guy` | Парень непринуждённый |
| `Lively_Girl`` | Живая девушка |
| `Patient_Man` | Терпеливый мужчина |
| `Young_Knight` | Молодой рыцарь |
| `Determined_Man` | Решительный мужчина |
| `Lovely_Girl` | Прелестная девушка |
| `Decent_Boy` | Приличный мальчик |
| `Imposing_Manner` | Внушительная манера |
| `Elegant_Man` | Элегантный мужчина |
| `Abbess` | Настоятельница |
| `Sweet_Girl_2` | Милая девушка 2 |
| `Exuberant_Girl` | Энергичная девушка |

### Кастомные голоса

Можно использовать голоса, созданные через MiniMax Voice Clone.

## Тестирование

### 9Router Dashboard (встроенный Playground)

1. Откройте `http://f9router.local/dashboard`
2. Перейдите в **Media Providers** → **TTS** → **MiniMax**
3. Выберите модель и голос
4. Введите текст
5. Нажмите **Generate**

### curl

```bash
curl -X POST http://localhost:5000/v1/audio/speech \
  -H "Authorization: Bearer sk-or-your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"model":"speech-2.8-hd","input":"Тест","voice":"English_expressive_narrator"}' \
  --output test.mp3
```

### Python

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:5000/v1",
    api_key="sk-or-your-api-key"
)

response = client.audio.speech.create(
    model="speech-2.8-hd",
    input="Тестовый текст",
    voice="English_expressive_narrator"
)

response.stream_to_file("test.mp3")
print("Сохранено: test.mp3")
```

## Ошибки

| HTTP статус | Код ошибки | Описание |
|---|---|---|
| 400 | `invalid_request_error` | Не указан model/input/voice |
| 401 | `authentication_error` | Невалидный API ключ |
| 402 | `billing_error` | Недостаточно средств |
| 404 | `invalid_request_error` | Модель не найдена |
| 429 | `rate_limit_error` | Превышен лимит запросов |
| 500 | `internal_error` | Внутренняя ошибка сервера |
| 503 | `service_unavailable` | Нет доступных нод 9Router |

## Зависимости

### Сервисы

| Сервис | Порт | Описание |
|---|---|---|
| AuthService | gRPC 5001 | Аутентификация API ключей |
| BillingService | gRPC 5002 | Проверка баланса, запись usage |
| ControlPlane | gRPC 5003 | Выбор нод 9Router |
| 9Router | HTTP 20127 | Проксирование к MiniMax API |

### Внешние API

| API | Endpoint | Описание |
|---|---|---|
| MiniMax TTS | `https://api.minimax.io/v1/t2a_v2` | Генерация речи |

## Известные ограничения

1. **Нет streaming**: TTS возвращает полный ответ (не SSE чанки)
2. **Биллинг по символам**: Не по токенам (MiniMax считает по символам)
3. **Нет проверки лимита символов**: MiniMax поддерживает до ~10K символов за запрос
4. **Нет кэширования**: Каждый запрос генерирует новое аудио
