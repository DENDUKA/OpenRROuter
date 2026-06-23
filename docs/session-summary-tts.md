# Итоги сессии: Добавление TTS в OpenRussiaRouter

> Дата: 2026-06-20
> Задача: Добавить поддержку Text-to-Speech (TTS) в OpenRussiaRouter

## Что было сделано

### 1. Анализ и исследования

- Изучена документация OpenAI TTS API
- Проанализирована архитектура 9Router и MiniMax TTS
- Исследован биллинг TTS в OpenRouter
- Определена формула тарификации: символы Unicode = tokens

### 2. Реализация в OpenRussiaRouter

#### Новые файлы

| Файл | Описание |
|---|---|
| `src/OpenRussiaRouter.ApiGateway/Application/Endpoints/TextToSpeechHandler.cs` | Полный хендлер TTS с валидацией, retry, биллингом |
| `docs/tts-implementation.md` | Документация по реализации |
| `docs/session-summary-tts.md` | Этот файл |

#### Модифицированные файлы

| Файл | Изменение |
|---|---|
| `IRouterProxyProvider.cs` | Добавлен `SendTextToSpeechAsync` |
| `YarpRouterProxyProvider.cs` | Реализация + константа `AudioSpeechPath` |
| `ApiGatewayExtensions.cs` | DI + `MapPost("/v1/audio/speech")` |
| `appsettings.json` | s2s токен для BillingService |

### 3. Реализация в 9Router

#### Модифицированные файлы

| Файл | Изменение |
|---|---|
| `open-sse/handlers/ttsProviders/minimax.js` | Принимает все параметры MiniMax API с дефолтами |
| `open-sse/handlers/ttsProviders/genericFormats.js` | Пробрасывает `params` в handler |
| `open-sse/handlers/ttsCore.js` | Пробрасывает `params` через цепочку |
| `src/sse/handlers/tts.js` | Извлекает все параметры из body |

## Ключевые решения

### Биллинг

- **Формула**: `cost = (characters / 1,000,000) × output_per_1m_usd`
- **Логика**: Символы Unicode входного текста = `output_tokens`
- **Причина**: MiniMax API не возвращает usage, тарификация по символам

### Параметры

- **Полная поддержка** всех параметров OpenAI TTS API
- **Дефолтные значения** для необязательных параметров
- **Проброс** всех параметров через цепочку: `tts.js → handleTtsCore → synthesizeViaConfig → minimaxTts`

### Трансформация model

- Клиент отправляет: `{ model: "speech-2.8-hd", voice: "alloy" }`
- 9Router ожидает: `{ model: "speech-2.8-hd/alloy" }`
- Наш gateway: авторизация по `model`, патч JSON с `routerModelId`

## Тестирование

### Тестовый запрос

```bash
curl -X POST http://localhost:5000/v1/audio/speech \
  -H "Authorization: Bearer sk-or-your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "speech-2.8-hd",
    "input": "Привет, это тест TTS API",
    "voice": "English_expressive_narrator"
  }' \
  --output test.mp3
```

### 9Router Dashboard

- URL: `http://f9router.local/dashboard/media-providers/tts/minimax`
- Playground с выбором модели, голоса, параметров

## Цены

| Модель | output_per_1m_usd | Пример (1K символов) |
|---|---|---|
| speech-2.8-hd | $48.61 | ~$0.049 |
| speech-2.8-turbo | $27.78 | ~$0.028 |

## Дальнейшие шаги

1. **UPSERT моделей** в BillingService для TTS
2. **Тестирование** с реальными запросами
3. **Мониторинг** usage и ошибок
4. **Оптимизация** retry логики
5. **Добавление** STT (Speech-to-Text) endpoints
