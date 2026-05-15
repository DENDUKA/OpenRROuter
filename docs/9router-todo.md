# 9Router — TODO: контракты для интеграции с OpenRussiaRouter

Этот файл описывает изменения, которые нужно сделать в форке 9Router,
чтобы он корректно работал как прокси-нода в системе OpenRussiaRouter.

---

## ROUTER-001 — Internal health endpoint

**Цель:** Control Plane периодически опрашивает ноду, чтобы знать, живая ли она.

**Что реализовать:**

```
GET /internal/health
Authorization: Bearer {NODE_TOKEN}
```

**Ответ 200 OK:**
```json
{
  "status": "ok",
  "timestamp": "2026-05-15T10:00:00.000Z"
}
```

**Ответ 401 Unauthorized** (если токен отсутствует или неверный):
```json
{
  "error": "unauthorized"
}
```

**NODE_TOKEN** берётся из переменной окружения `NODE_TOKEN` (уже должна быть задана через `.env` / docker-compose).

---

## ROUTER-002 — Стандартизированный usage payload

**Цель:** OpenRussiaRouter читает количество токенов из ответа 9Router, чтобы рассчитать стоимость запроса.

**Что обеспечить:**

### Non-streaming ответ

В теле ответа `/v1/chat/completions` (без стриминга) поле `usage` должно содержать:

```json
{
  "id": "chatcmpl-xxx",
  "object": "chat.completion",
  "choices": [...],
  "usage": {
    "prompt_tokens": 150,
    "completion_tokens": 80,
    "cached_tokens": 0
  }
}
```

### Streaming ответ

При запросе с `"stream": true` и `"stream_options": {"include_usage": true}` финальный `data:`-чанк
**перед `data: [DONE]`** должен содержать поле `usage`:

```
data: {"id":"chatcmpl-xxx","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":150,"completion_tokens":80,"cached_tokens":0}}

data: [DONE]
```

**Маппинг полей:**

| Поле в ответе 9Router | Смысл |
|---|---|
| `prompt_tokens` | Входные токены (входящий промпт) |
| `completion_tokens` | Выходные токены (сгенерированный ответ) |
| `cached_tokens` | Кешированные токены (если провайдер поддерживает) |

> OpenRussiaRouter принимает также `input_tokens`/`output_tokens`/`cache_tokens` как алиасы.
> Для надёжности лучше использовать `prompt_tokens` / `completion_tokens` / `cached_tokens`.

---

## ROUTER-003 — Стандартизированная форма ошибок

**Цель:** OpenRussiaRouter должен понимать, является ли ошибка временной (и можно ли ретраить на другую ноду).

**Что обеспечить:**

Все ошибки `/v1/chat/completions` должны возвращаться в форме:

```json
{
  "error": {
    "code": "upstream_error",
    "message": "The upstream LLM provider returned a 503 error",
    "retryable": true,
    "upstream_status": 503
  }
}
```

**Поля:**

| Поле | Тип | Описание |
|---|---|---|
| `code` | string | Машинный код ошибки (см. ниже) |
| `message` | string | Человекочитаемое описание |
| `retryable` | bool | `true` = Gateway может повторить запрос на другой ноде |
| `upstream_status` | int? | HTTP статус код от провайдера (если применимо) |

**Коды ошибок:**

| Код | HTTP статус | `retryable` | Когда использовать |
|---|---|---|---|
| `upstream_error` | 502 | true | Провайдер вернул ошибку |
| `upstream_timeout` | 504 | true | Провайдер не ответил вовремя |
| `upstream_unavailable` | 503 | true | Нода не может достучаться до провайдера |
| `model_not_found` | 400 | false | Запрошенная модель не поддерживается |
| `rate_limited` | 429 | true | Провайдер вернул rate limit |
| `invalid_request` | 400 | false | Некорректный формат запроса |
| `internal_error` | 500 | false | Внутренняя ошибка ноды |

---

## ROUTER-004 — Валидация node token на всех API-запросах

**Цель:** Запретить использование 9Router в обход OpenRussiaRouter. Каждый запрос к `/v1/` должен содержать токен ноды.

**Что реализовать:**

Middleware, которое проверяет заголовок `X-ORR-Node-Token` на всех маршрутах `/v1/*` и `/internal/*`:

```
POST /v1/chat/completions
X-ORR-Node-Token: {NODE_TOKEN}
```

Если заголовок отсутствует или токен неверный:

```
HTTP 401 Unauthorized
{"error": "unauthorized"}
```

**NODE_TOKEN** — статическая строка из переменной окружения `NODE_TOKEN`.

> Это отдельный токен от `JWT_SECRET` и `API_KEY_SECRET` из `.env`. 
> Должен быть добавлен в существующий `.env` файл на VPS как `NODE_TOKEN=<random_secret>`.

---

## ROUTER-005 — Маппинг моделей (internal ID → upstream provider ID)

**Цель:** OpenRussiaRouter хранит модели под своими внутренними ID (`openai/gpt-4o`, `anthropic/claude-opus-4`).
9Router должен уметь принять такой ID и передать нужному провайдеру под его ID.

**Что реализовать:**

Конфиг-файл или переменные окружения с таблицей маппинга:

```json
{
  "openai/gpt-4o": {
    "provider": "openai",
    "upstream_model": "gpt-4o"
  },
  "openai/gpt-4o-mini": {
    "provider": "openai",
    "upstream_model": "gpt-4o-mini"
  },
  "anthropic/claude-opus-4": {
    "provider": "anthropic",
    "upstream_model": "claude-opus-4-5"
  },
  "anthropic/claude-sonnet-4": {
    "provider": "anthropic",
    "upstream_model": "claude-sonnet-4-5"
  },
  "anthropic/claude-haiku-4": {
    "provider": "anthropic",
    "upstream_model": "claude-haiku-4-5"
  }
}
```

**Поведение:**

1. Запрос приходит с `"model": "openai/gpt-4o"`
2. 9Router ищет `openai/gpt-4o` в таблице маппинга
3. Подставляет `upstream_model: "gpt-4o"` в запрос к OpenAI
4. Если маппинг не найден → возвращает `{"error": {"code": "model_not_found", "retryable": false, ...}}`

---

## ROUTER-006 — Локальное хранилище API-ключей + middleware валидации

**Цель:** когда пользователь обращается к Gateway с `Authorization: Bearer sk-...`,
Gateway пробрасывает этот заголовок на 9Router, а нода проверяет ключ у себя локально
(без обратного вызова в OpenRussiaRouter). Это обеспечивает независимость нод от центрального
сервиса при каждом запросе.

---

### 6.1 — Эндпоинт синхронизации ключей

9Router должен принять от OpenRussiaRouter полный список активных ключей и обновить
своё локальное хранилище.

**Эндпоинт:**
```
POST /internal/keys/sync
Authorization: Bearer {NODE_TOKEN}
Content-Type: application/json
```

**Тело запроса:**
```json
{
  "keys": [
    { "prefix": "sk-abc1", "secret_hash": "sha256:e3b0c44298fc..." },
    { "prefix": "sk-def2", "secret_hash": "sha256:a87ff679a2f3..." }
  ]
}
```

- `prefix` — публичная часть ключа (первые N символов до `_`)
- `secret_hash` — SHA-256 от секретной части ключа в hex, с префиксом `sha256:`
- Вызов **полностью заменяет** хранилище (full replace, не merge)
- Возможна дополнительная инкрементная операция через `DELETE /internal/keys/{prefix}` для
  мгновенной блокировки отозванного ключа

**Ответ 200:**
```json
{ "synced": 42, "timestamp": "2026-05-15T10:00:00Z" }
```

**Ответ 401:** токен NODE_TOKEN неверный / отсутствует.

---

### 6.2 — Отзыв одного ключа

Для немедленной блокировки ключа без полной пересинхронизации:

```
DELETE /internal/keys/{prefix}
Authorization: Bearer {NODE_TOKEN}
```

**Ответ 200:** ключ удалён из хранилища.
**Ответ 404:** ключ с таким prefix не найден (идемпотентно — тоже 200 допустимо).

---

### 6.3 — Middleware валидации Bearer-токена на `/v1/*`

Все запросы к `/v1/chat/completions` и `/v1/models` должны проходить через middleware,
которое проверяет `Authorization: Bearer {raw_key}` против локального хранилища.

**Алгоритм валидации:**

```
1. Извлечь raw_key из заголовка Authorization: Bearer {raw_key}
2. Разделить raw_key по разделителю:
   - Формат ключа: {prefix}_{secret}  (пример: sk-abc1_xR7kLmN2...)
   - prefix = часть до первого '_'
   - secret = часть после первого '_'
3. Найти prefix в локальном хранилище → получить stored_hash
4. Вычислить sha256(secret) → compare с stored_hash
5. Совпадает → запрос проходит
6. Не совпадает / не найден → 401 Unauthorized
```

**Ответ 401:**
```json
{ "error": { "code": "invalid_api_key", "message": "Invalid API key", "retryable": false } }
```

> Middleware не заменяет NODE_TOKEN для внутренних `/internal/*` эндпоинтов.
> Схема аутентификации:
> - `/internal/*` → `X-ORR-Node-Token: {NODE_TOKEN}` (как и раньше)
> - `/v1/*` → `Authorization: Bearer {user_api_key}` (проверяется через локальное хранилище)

---

### 6.4 — Хранилище ключей

Хранить в памяти как `Map<prefix, secret_hash>`. При рестарте ноды — хранилище пустое;
OpenRussiaRouter должен синхронизировать при запуске (по событию регистрации ноды) и
периодически.

Опционально: сохранять на диск (SQLite / файл) для выживания рестарта без ожидания следующей синхронизации.

---

### Тесты (ROUTER-006):

- `POST /internal/keys/sync` без токена → 401
- `POST /internal/keys/sync` с корректным телом → хранилище обновлено
- `/v1/chat/completions` с ключом из хранилища → 200
- `/v1/chat/completions` с неизвестным prefix → 401
- `/v1/chat/completions` с верным prefix, но неверным secret → 401
- `DELETE /internal/keys/{prefix}` → ключ больше не проходит валидацию
- Полная синхронизация заменяет старые ключи (отозванные не остаются)

---

---

## ROUTER-007 — Заменить существующую auth-валидацию на проверку через синхронизированное хранилище

**Цель:** 9Router сейчас имеет собственную систему пользователей и API-ключей
(`JWT_SECRET`, `API_KEY_SECRET`, своя БД). В интеграции с OpenRussiaRouter пользователи
создают ключи **только в OpenRussiaRouter**. 9Router должен принимать эти ключи,
проверяя их против синхронизированного хранилища (ROUTER-006), а не против своей БД.

**Что изменить:**

### Существующий middleware (до изменения)
9Router проверяет `Authorization: Bearer {token}` через:
- Проверку JWT (session token из собственной авторизации)
- Или собственные API-ключи из `API_KEY_SECRET`

### Новый middleware (после изменения)
На маршрутах `/v1/*` (и только на них) заменить логику на:

```
1. Прочитать Authorization: Bearer {raw_key}
2. Разделить: prefix = часть до '_', secret = часть после '_'
3. Найти prefix в синхронизированном хранилище (ROUTER-006)
4. sha256(secret) == stored_hash → пропустить запрос
5. Иначе → 401
```

### Что НЕ меняется
- `/internal/*` — по-прежнему защищены `X-ORR-Node-Token` (статический токен ноды)
- Существующий UI 9Router (если есть) — может сохранить собственную сессионную авторизацию
- Роутинг к провайдерам (OpenAI / Anthropic) — 9Router использует **свои** provider API keys из env

### Env-переменные провайдеров (9Router сам хранит их)
9Router продолжает хранить ключи к LLM-провайдерам в своих env:
```
OPENAI_API_KEY=sk-...
ANTHROPIC_API_KEY=sk-ant-...
```
Пользовательский ключ OpenRussiaRouter (`sk-abc1_secret`) — это только для аутентификации
**пользователя** на входе в 9Router, а не для обращения к провайдеру.

### Маппинг ответственности

| Кто | За что отвечает |
|---|---|
| Пользователь | Предъявляет `sk-abc1_secret` (ключ из OpenRussiaRouter) |
| Gateway | Проверяет ключ у Auth Service, проверяет баланс |
| 9Router | Проверяет ключ против синхронизированного хранилища |
| 9Router | Подставляет свой `OPENAI_API_KEY` при обращении к OpenAI |

**Файлы для изменения в 9Router:**
- `middleware/auth.ts` (или аналогичный) — заменить логику валидации на `/v1/*`
- Убрать зависимость от `API_KEY_SECRET` для `/v1/*` маршрутов

**Тесты:**
- `/v1/chat/completions` с ключом из синхронизированного хранилища → 200
- `/v1/chat/completions` с ключом из собственной базы 9Router (старый формат) → 401
- `/v1/chat/completions` без заголовка → 401
- `/internal/health` по-прежнему требует `X-ORR-Node-Token`, а не Bearer

---

## Итог: переменные окружения, которые нужно добавить

| Переменная | Пример | Назначение |
|---|---|---|
| `NODE_TOKEN` | `nT_abc123xyz` | Токен для аутентификации запросов от Control Plane и Gateway |

Остальные переменные (`JWT_SECRET`, `INITIAL_PASSWORD`, `API_KEY_SECRET`, `MACHINE_ID_SALT`) уже существуют в `.env` на VPS.

---

## Зависимости для OpenRussiaRouter

До реализации этих задач:

- Control Plane health checks работают **без аутентификации** (нужно добавить токен в конфиг ноды)
- Billing расчёт стоимости работает только для **non-streaming** запросов (usage уже читается)
- **Streaming billing** не работает без ROUTER-002 (финальный usage chunk)
- Gateway может принять от 9Router любую форму ошибки, но retry-логика не сработает без `retryable` поля (ROUTER-003)
