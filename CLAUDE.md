# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> Общаемся и отвечаем пользователю **на русском языке**.

Это корневой индекс. У **каждого проекта в `src/` есть свой `CLAUDE.md`** с деталями
(bootstrap, слои, ключевые классы, подводные камни) — **читай его перед правкой сервиса**,
здесь только то, что видно лишь с уровня всего репозитория.

## Build & Test

```powershell
# Флаги -nr:false -m:1 обязательны: без них некоторые Windows-песочницы
# падают на MSBuild node reuse с бессмысленной ошибкой.
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build   OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test    OpenRussiaRouter.sln --no-build   -nr:false -m:1

# Один тестовый проект
dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1

# Один тест по имени
dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -filter "FullyQualifiedName~MyTest"

# Запуск сервиса локально
dotnet run --project src/OpenRussiaRouter.<Service>
```

SDK пиннится через `global.json` (8.0.420, `rollForward: latestFeature`).

### Инварианты сборки

- **Любой warning ломает build** — `Directory.Build.props`: `TreatWarningsAsErrors=true`,
  `AnalysisLevel=latest`. Чинить причину, не глушить точечными `#pragma`.
  NuGet `NU19xx` (транзитивные зависимости) не фейлят сборку.
- **Central Package Management** — `Directory.Packages.props` держит все версии.
  **Никогда не пиши `<PackageVersion>` в `.csproj`** — там только `<PackageReference Include="..." />`.
  Новый пакет = правка двух файлов.
- **Никакого Entity Framework нигде** — только Dapper + Npgsql. Не добавлять EF-пакеты,
  `DbContext`, EF-миграции.

### Тесты

- xUnit + FluentAssertions + Moq.
- **PostgreSQL-интеграционные тесты — на Testcontainers**: сами поднимают контейнер postgres,
  без Docker тихо скипаются. Переменные `ORR_{AUTH|BILLING|CONTROL}_POSTGRES_TEST_CONNECTION`
  только *переопределяют* строку подключения, они не включают тесты.
- **E2E — Playwright, opt-in**:
  ```powershell
  pwsh tests/OpenRussiaRouter.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
  # конфиг: tests/OpenRussiaRouter.E2ETests/e2e.local.json (gitignored)
  ```

### pre-push хук собирает Docker-образы

`.githooks/pre-push` запускает `build-push.ps1` при push в `main` — собирает и пушит
изменённые образы сервисов в GHCR. Включается один раз: `git config core.hooksPath .githooks`.
Хук **никогда не блокирует push** (при ошибке делает `exit 0`) — сборку образов проверяй отдельно.

## Архитектура

OpenAI-совместимый LLM-шлюз с $-биллингом: .NET 8 микросервисы, между собой — gRPC.

| Проект в `src/` | Роль | Порт |
|---|---|---|
| `OpenRussiaRouter.ApiGateway` | Публичный HTTP-вход, OpenAI-совместимый API. БД нет | HTTP 5000 |
| `OpenRussiaRouter.AuthService` | Пользователи, сессии, API-ключи, email-верификация, инвайты, внешний логин (Google/Yandex) | gRPC 5001 |
| `OpenRussiaRouter.BillingService` | Балансовый леджер, каталог моделей/цен, usage, платежи и FX, spend-лимиты, аномалии, теги | gRPC 5002 |
| `OpenRussiaRouter.ControlPlane` | Реестр нод 9Router, health, лизинг, capability-routing, синк ключей | gRPC 5003 |
| `OpenRussiaRouter.ContentService` | Каталог сторонних инструментов (Tools) для подключения к API; markdown-описания, картинки, s2s-защита админских RPC | gRPC 5007 |
| `OpenRussiaRouter.WebApp` | **Razor Pages** (не Blazor) — кабинет + админка, cookie-auth, чекаут платежей и вебхуки, локализация ru/en. Своей БД нет | HTTP 5004 |
| `OpenRussiaRouter.CollectorService` | Опрос нод/OpenRouter, in-memory кеш снапшотов. **БД нет**, состояние теряется при рестарте (by design) | gRPC 5005 |
| `OpenRussiaRouter.BuildingBlocks` | Money, TokenUsage, ошибки, миграции, телеметрия, gRPC-хелперы. Зависимостей на другие проекты solution нет | — |
| `OpenRussiaRouter.Contracts` | `.proto` (источник истины gRPC) + ручные DTO. Зависит только от BuildingBlocks | — |
| `OpenRussiaRouter.9Router` | Форк 9Router (ядро роутера). Своя `AGENTS.md`, относиться как к upstream | — |

### Поток `POST /v1/chat/completions`

0. Rate limit (`GatewayRateLimiting`, политика `gateway-chat`): fixed-window per-API-key
   с фолбэком per-IP, секция `RateLimiting`, при `Enabled=false` — no-op. Отказ — 429
   в OpenAI-формате ошибки + `Retry-After`.
1. Bearer-токен (`sk-or-*`) → валидация в AuthService (UserId, ApiKeyId, IsAdmin).
2. Баланс, spend-лимиты ключа (суточный/недельный) и доступность модели в BillingService.
   Здесь публичный `ModelId` (`ORR/GLM-4.7`) резолвится во внутренний `RouterModelId` (`glm-4.7`).
3. Выбор ноды в ControlPlane — **передаётся `RouterModelId`**, иначе capability-store
   не сматчит модель. Capability-aware, при пустом store — fallback на least-load.
4. Патч тела: `model` заменяется на `RouterModelId`. Если id модели содержит "gemini" —
   `GeminiSchemaPatcher` инлайнит `$ref`/`$defs` в схемах тулов (Gemini их не принимает).
5. Проксирование: YARP (non-streaming) либо `HttpCompletionOption.ResponseHeadersRead` (streaming).
   **Один retry** на другую ноду при 5xx/сетевой ошибке — только пока первый байт не ушёл клиенту.
6. Usage вынимается из JSON-тела или из финального SSE-чанка (`include_usage`).
7. Событие usage кладётся в **PostgreSQL-outbox** (`db/migrations/gateway/`,
   `PostgresUsageOutboxRepository` + `UsageOutboxDispatcher`), лиза освобождается.

Эндпоинты: `/v1/chat/completions`, `/v1/chat/completions/stream` (явный stream-алиас),
`/v1/models`, `/health`, `/ready`.

### Общие для всех сервисов паттерны

- **Repository**: `I{Service}Repository` с двумя реализациями — Dapper/Postgres и in-memory
  (in-memory используется в unit-тестах).
- **Регистрация в DI**: extension-методы `Add{Service}Infrastructure()` / `Add{Service}Application()`
  в `Infrastructure/Startup/`.
- **SQL-миграции**: сырые `.sql` в `db/migrations/{auth|billing|control|gateway|analytics|content}/`,
  применяются на старте через `PostgresSqlMigrationRunner`. Новый файл — со следующим
  числовым префиксом. Файлы неизменяемы: применённую миграцию не редактируем, добавляем новую.
- **gRPC-контракты**: `.proto` в `src/OpenRussiaRouter.Contracts/Protos/` —
  `auth.proto`, `billing.proto`, `control-plane.proto`, `collector.proto`, `content.proto`.
- **gRPC-инфраструктура** (`BuildingBlocks/Grpc/`): `GrpcDeadlineInterceptor`
  (`GrpcServices:CallDeadlineSeconds`, дефолт 30) на всех клиентах; `ServiceAuthClientInterceptor`
  ↔ `ServiceAuthInterceptor` для межсервисной аутентификации.
  Structured gRPC error details пока нет (PLATFORM-002).
- **Correlation ID**: заголовок `X-Correlation-Id` → `CorrelationIdReader` → контекст Serilog.
- **Конфигурация gRPC-адресов**: секция `GrpcServices` в `appsettings.json` потребителя
  (`Auth` → `http://localhost:5001`, `Billing` → `:5002`, `ControlPlane` → `:5003`,
  `Collector` → `:5005`, `Content` → `:5007`).
- **Порт 5006 не занимать**: в compose (`deploy/compose/compose.yml`, `compose.prod.yml`) это
  HTTP-эндпоинт коллектора (`5006:8080`), по нему же ходит health-чек в `update-orr.sh`.
  Новым сервисам — следующий свободный порт.

### Карта страниц WebApp

Публичные: `/login`, `/signup` → `/verify-email`, `/forgot-password` → `/reset-password`,
`/invite/{token}`, `/models`, `/models/{id}`, `/providers`, `/providers/{id}`, `/tools`,
`/tools/{id}`, `/docs`, `/support`, `/legal/offer`, `/legal/privacy`, `/pay/mock`.

Кабинет: `/` (обзор баланса), `/workspace/api-keys`, `/workspace/api-keys/limits`,
`/workspace/activity`, `/workspace/credits`, `/workspace/payment-success`, `/workspace/settings`.

Админка: `/admin`, `/admin/users`, `/admin/user-edit`, `/admin/topups`, `/admin/models`, `/admin/models-sync`,
`/admin/tags`, `/admin/providers`, `/admin/tools`, `/admin/tools/edit/{id?}`, `/admin/nodes`, `/admin/nodes/{id}`,
`/admin/nodes/{id}/limits`, `/admin/node-edit`, `/admin/anomalies`, `/admin/requests`, `/admin/broadcast`,
`/admin/gateway-test`.

## Платежи и пополнение баланса

Разделение ответственности жёсткое, не нарушать:

- **BillingService — единственный владелец денег.** Считает `credited_usd` по собственной
  доверенной FX-котировке (`FxQuoteService`, источник ЦБ РФ + `MarkupPercent`, фолбэк-курсы
  в `ExchangeRateOptions.Fallback`, котировка живёт 48ч). Авторитетные min/max суммы пополнения —
  тоже здесь (`PaymentOptions`). Таблица платежей — миграции `db/migrations/billing/013_payments.sql`
  и `016_promo_settings_and_payment_fields.sql`.
- **WebApp — только оркестрация чекаута и приём вебхуков.** Страницы вызывают
  **только `PaymentCheckoutService`**, никогда `BillingProvider.CreatePayment` / `AttachProviderRef`
  напрямую. Флоу: Billing создаёт строку и `payment_id` (= `order_id`) → провайдер отдаёт свой id
  и pay URL → `AttachProviderRef` атомарно их привязывает.
- **Провайдеры** за `IPaymentProvider` + `PaymentProviderRegistry`: `mock` (только dev) и
  `rollypay` (СБП-агрегатор, `Services/Payments/Rollypay/`). Активный выбирается
  `Payments:ActiveProvider`; пустое значение просто выключает платежи.
- **`PaymentStartupGuard`** падает на старте, если вне Development активен `mock` или
  неизвестный провайдер. Не ослаблять — мок не должен быть доступен в проде.
- **Вебхук** — `PaymentWebhookEndpoint.Map(app)`: анонимный (провайдер не может залогиниться),
  per-IP rate limit, лимит размера тела, **verify-then-parse по сырому телу**
  (`RollypayWebhookVerifier`). Незаконфигуренный секрет → 401, а не пропуск.
- **`PaymentReconciliationWorker`** добирает статусы: `Created` без оплаты уходит в `Expired`.

Страницы: `/workspace/credits` (пополнение), `/pay/mock` (dev-заглушка оплаты),
`/workspace/payment-success`, `/admin/topups` (история пополнений по всем пользователям).

## Лимиты, метрики, продуктовая аналитика

- **`LimitMetrics`** (`BuildingBlocks/Metrics/`) — единый счётчик отказов по лимитам,
  в Prometheus как `orr_limit_rejections_total`. Метки узкие (`limit_type` × `subject`,
  ~5 серий). **В лейблы нельзя класть api key id / user id / ip** — неограниченная
  кардинальность на публичном эндпоинте. Разрезы «кто именно упёрся» берём SQL-панелями
  поверх `billing.usage_records`, не метриками.
- **Продуктовая аналитика** (`BuildingBlocks/Analytics/`): очередь + фоновой флаш
  (`ProductAnalyticsEventQueue` → `ProductAnalyticsFlushWorker`) в `analytics.events`.
  Есть no-op реализация, псевдонимизация пользователя (`UserPseudonymizer`) и путь стирания
  (`IProductAnalyticsErasure`). Схема — `db/migrations/analytics/`.
- Дашборды Grafana и конфиг Prometheus для прода — в `deploy/compose/grafana/`
  и `deploy/compose/prometheus.prod.yml`.

## Два типа токенов 9Router — не путать

1. **Control Token** (на ноду). Лежит в `control.router_nodes.control_token` как
   **само значение токена**, а не имя секрета. Уходит как есть в
   `Authorization: Bearer <control_token>` к admin API ноды (`/api/integrations/*`).
   Никакого резолвинга через конфиг в `RouterNodeProvider` нет — и не надо
   реинтродьюсить `RouterHealthChecks:NodeTokens:{name}` (это было ошибкой в старой доке).
2. **Пользовательские API-ключи** (`sk-or-*`). Создаются в WebApp, хеш — в `auth.api_keys`,
   при создании пушатся на активные ноды, при отзыве деактивируются. Маппинг
   `(api_key_id, node_id, router_key_id)` — в `control.integration_key_sync`.

## Деплой — граница ответственности

- **GitHub Actions** (`.github/workflows/build-deploy.yml`, прод — `deploy-prod.yml`):
  restore → build → test → push образов в `ghcr.io/denduka/<service>` с тегами
  `sha-<commit>` и `dev-latest`. Неизменённые сервисы пропускаются по path-детекту.
- **ContentService** добавлен во все compose-файлы (корневой `docker-compose.yml`,
  `deploy/compose/compose.yml`, `compose.prod.yml`), `prometheus.prod.yml`, CI workflow
  (`build-deploy.yml`, `deploy-prod.yml`), `build-push.ps1`, `update-orr.sh`/`update-prod.sh`,
  и kustomize-манифесты (`base/services/content-service.yaml`, overlay dev, postgres-secret,
  migration-job, network-policies, image-list в `orr-dev-app.yaml`).
- **Argo CD Image Updater** (в кластере) следит за `dev-latest`, резолвит в digest,
  пишет image-оверрайды, дальше Argo CD синкает `orr-services`.
  Ожидаемые Argo CD applications после бутстрапа: `orr-infra`, `orr-services`,
  `orr-argocd-image-updater` (манифесты — в `infra/argocd/`).
- **Нельзя** добавлять в GHA шаг, который коммитит теги образов в Kustomize-манифесты
  или дёргает Argo CD. Этим владеет Image Updater.
- Argo CD на dev с `selfHeal: true` → **`kubectl set env` / `kubectl edit` откатываются за ~1 мин**.
  Любая правка env — только через `infra/kustomize/overlays/dev/services/*-env-patch.yaml` + git.
- Health-чек ноды 9Router: в dev-оверлее обязателен `RouterHealthChecks__HealthPath=/api/health`.
  Базовый дефолт `/v1/models` отдаёт 401 от f9router, и нода никогда не станет Healthy.

Полная пошаговая регистрация ноды 9Router в kind-кластере (секреты, endpoint URL,
маппинг `RouterNodeTokens`, ручной пуш ключа) — в [README.md](README.md), раздел
«9Router node setup in Kubernetes». Локальный k8s-runbook — `docs/argo-k8s-dev-runbook.md`,
прочее — в `docs/`.

## Известные пробелы

Актуальный список задач — в `TODO.md` и `docs/todo.md` (этот раздел устаревает быстрее всего;
перед тем как на него опереться — проверь по коду).

| Область | Пробел |
|---|---|
| gRPC | Нет structured error details (PLATFORM-002) |
| Логи | Нет хелпера для redact чувствительных полей |
| Billing | `billing.model_prices` — одна активная строка на модель, без versioned history (BILLING-003) |
| ControlPlane | Нет cooldown/anti-flapping в `RouterHealthCheckWorker` (CONTROL-002) |
| ControlPlane | После рестарта in-flight счётчики лиз сбрасываются, recovery нет (CONTROL-004) |
