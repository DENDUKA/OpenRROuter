# Нагрузочное тестирование ApiGateway (k6)

**Дата:** 2026-07-29
**Статус:** Approved (дизайн согласован с пользователем)

## 1. Цель и масштаб

Проверить стабильность OpenRussiaRouter под **ожидаемой пиковой нагрузкой ~80 одновременных
API-пользователей**. Это не стресс-тест до разрушения и не поиск предела, а подтверждение, что
под целевым пиком система стабильна, latency в норме, rate-limit/retry/outbox работают корректно.

80 одновременных пользователей chat-completions при времени ответа LLM 3–10 с соответствуют
примерно **10–25 RPS** на `/v1/chat/completions` (в полёте одновременно 30–250 запросов).

## 2. Окружение и ограничения

- **Цель теста:** продакшн LLM-API `https://api.openrussiarouter.com` (Caddy маршрутизирует
  поддомен `api.` → `api-gateway:8080`; apex `rawrter.com` обслуживает WebApp/Grafana —
  это **не** цель теста). На момент тестирования живых
  пользователей нет — риск «уронить сервис для живых людей» отсутствует.
- **Прод-топология** (`deploy/compose/compose.prod.yml`): один VPS `201.51.6.246`, каждый
  .NET-сервис в одном инстансе, ApiGateway зажат в `0.75 CPU / 512 MB`, спереди Caddy (TLS).
  Это **маленький** сервер — нагрузка выше ~25–40 RPS скорее всего упрётся в CPU/RAM одного инстанса.
- **Upstream — реальные вызовы LLM** через ноды 9Router. Используем модели **gemini-3.5-flash /
  gemini-3-flash / gemini-3.1-pro** (доступны без ограничений по стоимости). Каждый тестовый
  запрос ставит `max_tokens: 1` и короткий промпт — стоимость пренебрежимо мала, но usage
  пишется в биллинг/аналитику как реальный (это учтено — тестовый пользователь изолирован).
- **Генератор нагрузки — k6** (Grafana Labs, Go-engine, сценарии на JS): умеет SSE-стриминг,
  встроенные `thresholds`/`checks`, экспорт метрик в Prometheus/Grafana (уже стоят на проду).
  k6 запускается **с машины оператора**, не с прода — чтобы не отъедать CPU у тестируемого хоста.

### 2.1. Критическое ограничение: rate-limit

Прод-rate-limit: `RateLimiting:PermitLimit=60`, `WindowSeconds=60` → **60 запросов/мин на
API-ключ = 1 RPS per key** (`src/OpenRussiaRouter.ApiGateway/appsettings.json`,
`GatewayRateLimiting.cs`, партиция `key:<SHA256(apiKey)[..8]>`).

Один ключ задушит тест на 1 RPS. Решение: **пул из ~50 API-ключей** под одним тестовым
пользователем, k6 раздаёт по одному ключу на виртуального пользователяа (round-robin). Это
реалистично (в проде у каждого пользователя свой ключ) и не затрагивает конфиг прода.

## 3. Подготовка данных

1. **Тестовый пользователь** в auth с положительным балансом в billing (иначе пайплайн вернёт
   `402 billing_error`). Админ-ключ не нужен — обычный пользователь даёт реалистичный путь
   (включая spend-лимиты ключа).
2. **Пул из 50 API-ключей** (`sk-or-*`) под тестовым пользователем. Создаются через WebApp
   (`/workspace/apikeys`) либо напрямую. Список ключей оператор кладёт в `load/.env`
   (gitignored) как `K6_API_KEYS=key1,key2,...,key50`.
3. **Модель:** публичный `model_id` gemini-flash из каталога. Сверено с прода через
   `GET https://api.openrussiarouter.com/v1/models` (эндпоинт публичный, без Bearer):
   доступны `gemini-3-flash`, `gemini-3.1-pro`, `gemini-3.5-flash` (плоские ID, **без**
   префикса провайдера). Целевая — **`gemini-3.5-flash`**. Кладётся в `load/.env` как
   `K6_MODEL_ID`. `gemini-3.5-flash` поддерживает reasoning (efforts `["none","high"]`,
   дефолт `high`) — в теле запроса явно ставим `"reasoning_effort":"none"`, чтобы
   минимизировать латенси и cost (иначе `ReasoningRequestPatcher` подставит дефолт `high`).
4. **`.env.example`** хранится в репо с заглушками; реальный `.env` — gitignored.

## 4. Сценарии k6

Все сценарии лежат в `load/scenarios/`, общая библиотека — `load/lib/`. Переменные окружения
парсятся в `lib/config.js`. Каждый сценарий читает `K6_BASE_URL` (дефолт
`https://api.openrussiarouter.com`), `K6_API_KEYS` (через запятую → массив), `K6_MODEL_ID`
(дефолт `gemini-3.5-flash`).

| Сценарий | Файл | Профиль | Назначение |
|---|---|---|---|
| **smoke** | `scenarios/smoke.js` | 1 VU, 1 итерация | Запускать ВСЕГДА первым. Non-stream `/v1/chat/completions`. Подтверждает, что ключи/баланс/модель работают, до подачи нагрузки. Падение smoke = стоп, дальше не гонять. |
| **chat-ramp** | `scenarios/chat-ramp.js` | `ramping-arrival-rate`: 1 → 25 RPS за 5 мин, steady 25 RPS 5 мин, спуск до 0 за 2 мин (~12 мин) | Основной сценарий. Показывает кривую деградации: где latency начинает расти, при каком RPS появляются первые 5xx/429. |
| **chat-stream** | `scenarios/chat-stream.js` | `constant-vus`: 20 VU, длительность 5 мин | Отдельный путь `/v1/chat/completions/stream` (другой хендлер `HandleStreamAsync`, долгоживущие SSE-соединения). Кастомная метрика `ttfb` (time-to-first-byte). |
| **soak** | `scenarios/soak.js` | `constant-arrival-rate`: 15 RPS, 15 мин | Устойчивость во времени: утечки памяти, дрейф outbox-диспетчера (`gateway.usage_outbox`), рост latency со временем. |
| **models-get** | `scenarios/models-get.js` | `constant-arrival-rate`: 50 RPS, 3 мин | Лёгкий `GET /v1/models` (не биллингится, не ходит к upstream). Показывает потолок самой инфраструктуры шлюза без LLM. |

### 4.1. Тела запросов

Non-stream `/v1/chat/completions`:
```json
{
  "model": "${MODEL_ID}",
  "messages": [{"role": "user", "content": "hi"}],
  "max_tokens": 1,
  "reasoning_effort": "none",
  "stream": false
}
```

Stream `/v1/chat/completions/stream` — то же тело, но путь `/stream` (шлюз сам выставит
`stream:true` + `stream_options.include_usage:true` через `InjectStreamOptions`).

`max_tokens: 1` держит стоимость и время ответа минимальными, сохраняя end-to-end путь
(авторизация → баланс → выбор ноды → патч тела → прокси → usage-outbox).

### 4.2. Заголовки

`Authorization: Bearer <key>` (один ключ из пула на VU, round-robin), `Content-Type: application/json`.
k6 генерирует уникальный `X-Correlation-Id` на запрос (опционально — шлюз и так его создаёт).

## 5. Метрики и SLO (thresholds)

k6 `thresholds` с `abortOnFail` — тест останавливается при нарушении (кроме smoke, который
просто репортит):

| Метрика | Threshold | Применяется к сценариям |
|---|---|---|
| `http_req_failed` (5xx + сетевые; 429 — отдельно) | `< 1%` | все, кроме models-get |
| `http_req_duration` p95 | `< 15000` (15 с, upstream-bound) | chat-ramp, chat-stream, soak |
| `http_req_duration` p99 | `< 30000` (30 с) | chat-ramp, soak |
| `iterations` (доля успешных) | `> 95%` | chat-ramp, soak |
| кастомный `ttfb` p95 (stream) | `< 3000` (3 с) | chat-stream |
| `http_req_failed` (models-get) | `< 0.1%` | models-get |

429 не считаем «провалом SLO» в chat-ramp выше rate-limit-порога ключей (это ожидаемое
поведение политики `gateway-chat`) — но отдельно репортим rate `http_reqs{expected_response:false}`.

### 5.1. Параллельное наблюдение за продом

Во время прогона оператор держит открытыми (через ssh-туннель `201.51.6.246`) дашборды Grafana:
- CPU/RAM контейнеров `api-gateway`, `auth-service`, `billing-service`, `control-plane`, `postgres`;
- `orr_http_requests_total` / `orr_http_request_duration_seconds` (VisitorMetricsMiddleware);
- `orr_limit_rejections_total` (LimitMetrics);
- рост `gateway.usage_outbox` (через Sequence-запрос или дашборд);
- Postgres-пулы (`pg_stat_activity`).

Опционально: k6 `--out experimental-prometheus-rw` шлёт метрики прямо в прод-Prometheus, тогда
накладка нагрузки видна на тех же графиках.

## 6. Безопасность и стоп-условия

- **Порядок запуска:** всегда `smoke` → (только при успехе) целевой сценарий.
- `thresholds.abortOnFail` стопит тест при error_rate > 5% или p95 > 30 с автоматически.
- Под рукой: `update-prod.sh` / `deploy-prod.sh` для рестарта прода на случай падения сервиса.
- k6 на машине оператора, не на проде.
- Не гонять `chat-ramp`/`soak` одновременно — только по одному тяжёлому сценарию за раз.
- Тестовый пользователь изолирован; usage-записи от него видны в `billing.usage_records` —
  при необходимости чистятся отдельной миграцией (не в рамках этой спеки).

## 7. Структура файлов

```
load/
  README.md              — как готовить ключи, сверять модель, запускать сценарии
  lib/
    config.js            — парсинг env: BASE_URL, API_KEYS[], MODEL_ID, thresholds
    chat-body.js         — функции-конструкторы тел запросов (stream/non-stream)
  scenarios/
    smoke.js
    chat-ramp.js
    chat-stream.js
    soak.js
    models-get.js
  .env.example           — K6_BASE_URL, K6_API_KEYS, K6_MODEL_ID (заглушки; реальные данные — в gitignored .env)
  .gitignore             — .env
```

Это отдельный JS-проект, не входит в .NET solution; `dotnet build` его не трогает.
k6 ставится одним бинарником (`winget install k6` или `choco install k6` на Windows).

## 8. Что НЕ входит в рамки (out of scope)

- Стресс-тест до разрушения / поиск абсолютного предела — отдельная задача.
- Нагрузка на WebApp (Razor Pages), gRPC-эндпоинты сервисов напрямую, CollectorService.
- Тесты `/v1/images/generations` (не биллятся, отдельный хендлер) и `/v1/audio/speech`.
- Авторасширение/scaling-эксперименты (увеличение реплик/CPU) — это следующий шаг после
  получения baseline-цифр.
- Автоматизация в CI — прогоны ручные, с машины оператора.

## 9. Замечание безопасности

SSH root-доступ к прод-хосту передавался в чате открытым текстом. После завершения работ по
нагрузочному тестированию **пароль root на `201.51.6.246` необходимо сменить**. Учётные данные
нигде в репозитории не сохраняются. SSH-доступ нужен только на этапе исполнения (наблюдение
Grafana через туннель, проверка состояния контейнеров, рестарт при падении) — не для написания
сценариев.
