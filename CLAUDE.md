# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> Общаемся и отвечаем пользователю **на русском языке**.

Это корневой индекс. У **каждого проекта в `src/` есть свой `CLAUDE.md`** с деталями
(bootstrap, слои, ключевые классы, подводные камни) — **читай его перед правкой сервиса**,
здесь только то, что видно лишь с уровня всего репозитория.

## Reasoning capability flow (2026-08-29)

9Router is the source of truth for per-model public reasoning levels through
`caps.reasoningEfforts` in its admin catalog. The additive Collector protobuf field carries
that list to WebApp; Billing persists the effective list and whether an administrator narrowed
it (`ReasoningEffortsOverridden`). ApiGateway accepts client reasoning fail-soft: invalid values
fall back to the model default, and fields are stripped for models without an effective list.
`/v1/models` exposes `supported_efforts` and `default_effort` only for controllable models.
For a 9Router combo, `caps.reasoningEfforts` is the fail-closed intersection of every fallback
model's levels: the fallback lists must match exactly, otherwise the effective list is empty.
It is never a union or the capability of the first fallback. The accompanying
`comboReasoningMembers` diagnostic list is a deterministic node snapshot for the admin UI.

## Agent workflow — обязательно

Эти правила сильнее любых других инструкций в этом файле. Нарушать нельзя.

- **Перед правкой сервиса** — прочитай его `CLAUDE.md`:
  `src/OpenRussiaRouter.{ApiGateway|AuthService|BillingService|ControlPlane|ContentService|CollectorService|WebApp}/CLAUDE.md`.
  Если правка касается общих библиотек — также `src/OpenRussiaRouter.BuildingBlocks/CLAUDE.md`
  и `src/OpenRussiaRouter.Contracts/CLAUDE.md`. В этих файлах — специфичные для сервиса паттерны,
  ловушки, порты,契约. Не правь сервис, не открыв его `CLAUDE.md`.
  Быстрый путь — команда `/svc <Service>` (читает нужные CLAUDE.md и выдаёт отчёт по
  observation-контракту); триггер на автоправку — skill `orr-service-edit`.
- **После завершения задачи** — обязательно проверь, что решение собирается:
  ```powershell
  dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
  ```
  `TreatWarningsAsErrors=true` — любой warning = ошибка сборки. **Не сдавай задачу с падающим build.**
  Чини причину warning'а, не глушить `#pragma`.
- **После завершения задачи — обнови документацию:**
  - изменения, затрагивающие весь проект/архитектуру/поток запроса/инварианты → в **этот корневой `CLAUDE.md`**;
  - изменения, специфичные для сервиса (порты, ключевые классы, паттерны, подводные камни) → в
    **`src/OpenRussiaRouter.<Service>/CLAUDE.md`** соответствующего сервиса.
  - Не описывай мелкие правки (косметический рефакторинг без смены контракта) — только то, что
    меняет контракты, инварианты, поток, порты, паттерны, подводные камни.

## Agent harness (`.opencode/`)

Действия агента зафиксированы в командах и skill'ах (авто-обнаруживаются из `.opencode/`):

| Элемент | Тип | Назначение |
|---|---|---|
| `/svc <Service>` | command | Подготовка к правке сервиса: читает его `CLAUDE.md` + shared-библиотек, отчёт по observation-контракту. Реализует правило «Agent workflow» |
| `/migrate <area>` | command | Микро-инструмент для high-risk SQL-миграций: следующий числовой префикс, иммутабельность, шаблон |
| `/build-test [Service\|all]` | command | Сборка + тесты solution'а или одного сервиса с обязательными флагами |
| `/deploy [Service\|all]` | command | Деплой изменившихся сервисов на прод через `update-orr.sh` |
| `/add-task <title>` | command | Добавить задачу в `.opencode/tasks.md` со следующим ID, `status=todo` |
| `/start-task [<PREFIX>-NNN]` | command | Запустить отдельный агент `task-worker` (через task tool): берёт свободную задачу, помечает `in_progress`, реализует, собирает, переносит выполненную в архив |
| `orr-service-edit` | skill | On-demand триггер: при правке `src/OpenRussiaRouter.*/` напоминает открыть per-service `CLAUDE.md`. Это маршрутизатор к файлам, не копия |
| `task-worker` | agent (subagent) | Автономный исполнитель одной задачи из `.opencode/tasks.md`; владеет циклом claim→implement→build→archive. Запускается через `/start-task` |

### Файлы задач — два, и задача живёт ровно в одном

- **`.opencode/tasks.md` — активный бэклог.** Только `todo`, `in_progress`, `blocked`.
  Все новые задачи (`/add-task`) пишутся сюда.
- **`.opencode/tasks-done.md` — архив выполненных.** Только на дозапись, вручную не редактируется.

**Перевод в `done` — это перенос, а не смена строки статуса.** `task-worker` проставляет
`status: done` + `completed`, **дописывает блок целиком в конец `tasks-done.md`** и только
после этого **вырезает его из `tasks.md`**. Порядок именно такой: падение между шагами даёт
дубль (чинится вручную), а не потерянную задачу. `status: done` в `tasks.md` — признак
незавершённого переноса.

`blocked` в архив **не** переезжает — это не «выполнено», такие задачи остаются в бэклоге
с `blocked_reason`.

**ID не переиспользуются.** Следующий номер `/add-task` считает по максимуму **обоих** файлов
в рамках префикса (`<PREFIX>-NNN`; `T-` — разовые задачи по репозиторию, остальные префиксы —
свои пачки со своей нумерацией с 001). Считать только по `tasks.md` нельзя — номера
выполненных задач там уже не видны, и новая задача переиспользует чужой ID.

Контракты observation/error-recovery (status / summary / next_actions / artifacts; root cause /
safe retry / stop condition) зашиты в тела команд по скилу `agent-harness-construction`.

**Permissions** (`opencode.jsonc`): автопилот для всей обычной работы; необратимые операции
(force-push, `--amend`, `git reset --hard`, удаление веток/контейнеров/`volume`, `kubectl delete`,
`dropdb`, `DROP DATABASE|TABLE`, `TRUNCATE`) переведены в `ask`. Порядок правил bash: catch-all
`*: allow` первым, узкие `ask` последними (opencode: выигрывает **последнее** совпадение).

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
| `OpenRussiaRouter.ApiGateway` | Публичный HTTP-вход, OpenAI-совместимый API. Владеет схемой `gateway` (usage-outbox), миграции катает на старте | HTTP (в контейнере 8080) |
| `OpenRussiaRouter.AuthService` | Пользователи, сессии, API-ключи, email-верификация, инвайты, внешний логин (Google/Yandex) | gRPC 5001 |
| `OpenRussiaRouter.BillingService` | Балансовый леджер, каталог моделей/цен, usage, платежи и FX, spend-лимиты, аномалии, теги | gRPC 5002 |
| `OpenRussiaRouter.ControlPlane` | Реестр нод 9Router, health, лизинг, capability-routing, синк ключей | gRPC 5003 |
| `OpenRussiaRouter.ContentService` | Каталог сторонних инструментов (Tools) для подключения к API; markdown-описания, картинки, s2s-защита админских RPC | gRPC 5007 |
| `OpenRussiaRouter.WebApp` | **Razor Pages** (не Blazor) — кабинет + админка, cookie-auth, чекаут платежей и вебхуки, локализация ru/en. Своей БД нет | HTTP 5004 |
| `OpenRussiaRouter.CollectorService` | Опрос нод/OpenRouter, in-memory кеш снапшотов. **БД нет**, состояние теряется при рестарте (by design) | gRPC 5005 |
| `OpenRussiaRouter.BuildingBlocks` | Money, TokenUsage, ошибки, миграции, телеметрия, gRPC-хелперы. Зависимостей на другие проекты solution нет | — |
| `OpenRussiaRouter.Contracts` | `.proto` (источник истины gRPC) + ручные DTO. Зависит только от BuildingBlocks | — |

**9Router в репозитории исходниками не лежит** — вопреки старой доке, папки `src/OpenRussiaRouter.9Router`
нет и в solution он не входит. Это внешний компонент: образ f9router (`data/f9router-docker`,
`deploy/compose/update-9router.sh`), ранбуки — `docs/9router-*.md`. Ноды 9Router видны системе
только через ControlPlane и их admin API.

Вне solution, но в репозитории: `docs-site/` — публичная документация на **VitePress**
(отдельный npm-проект со своим `Dockerfile`, `npm run docs:dev` / `docs:build`;
`dotnet build` его не собирает, CI-шаги для .NET его не трогают), `db/migrations/` — SQL-миграции,
`deploy/` — compose+Caddy+Grafana для VPS, `infra/` — kustomize и Argo CD.
`tools/` (в т.ч. разовая утилита наполнения провайдеров `fill-providers`) **в git не хранится** —
каталог целиком в `.gitignore`, это локальная песочница.

#### SEO-контур `docs-site` (`.vitepress/config.mts`)

Документация смонтирована под `/docs/` (Caddy: `@docs path /docs /docs/*` → контейнер `docs`,
префикс **не** стрипается), поэтому у неё свой, независимый от WebApp набор SEO-сигналов:

- **`hostname` в опции `sitemap` обязан включать `/docs/`** — VitePress склеивает его с маршрутом
  как есть и `base` сам не подмешивает. С голым origin в `/docs/sitemap.xml` уезжают URL без
  префикса, то есть 404 на каждой строке.
- **Опция `sitemap` не добавляет canonical** — canonical, OG/Twitter и hreflang проставляет
  `transformHead`.
- **У документации свой набор локальных URL** — `/docs/` (ru) и `/docs/en/`, деревья зеркальны
  1:1. hreflang взаимные + self-referencing; 404 исключена (у неё нет пары). Ломается
  зеркальность — ломается кластер. Английское дерево вложено **внутрь базы `/docs/`**, а не под
  сайтовый префикс `/en`, поэтому WebApp отдаёт `/en/docs*` редиректом на `/docs/en*` и в свою
  карту сайта английскую документацию не кладёт.
- **`x-default` в docs-site указывает на русскую версию, в WebApp — на английскую.** Расхождение
  намеренное и его не надо «выравнивать»: у документации это исторический выбор, у сайта
  x-default обслуживает рынки за пределами русскоязычного (см. SEO-024).
- **Обе карты сайта перечислены в `robots.txt` WebApp**: динамическая `/sitemap.xml` знает про
  `/docs/` ровно один URL (конечный URL документации) и содержимое раздела не покрывает.
- **Счётчик GA4 у документации свой, но поток — общий с сайтом.** `head` в `config.mts` ставит
  тот же `G-38042Y9GW4`, что и layout WebApp: layout сюда не доезжает (отдельный контейнер),
  а отдельное свойство рвало бы сессию на переходе сайт → документация. Consent Mode
  выставляется **до** `gtag.js` по cookie `orr_consent` (её пишет баннер WebApp, тот же origin);
  своего баннера у документации нет — зашедший сразу на `/docs` остаётся в `denied`.
  Переходы внутри VitePress — `pushState`, их считает Enhanced Measurement свойства
  («page changes based on browser history events»); ручной `page_view` не шлём, он дал бы дубль.
  Метрики (`ym`) в документации по-прежнему нет.
- Юридических страниц в docs-site **нет** — их единственный дом это `/legal/*` в WebApp
  (причина — в [WebApp/CLAUDE.md](src/OpenRussiaRouter.WebApp/CLAUDE.md)).
- **Артефакты сборки в git не хранятся** — `node_modules/` и `.vitepress/{dist,cache}/` закрыты
  корневым `.gitignore` (шаблоны с `**/`: без него путь со слэшем якорится к корню репозитория
  и `docs-site/` не попадает под правило) и продублированы в `docs-site/.dockerignore`.
  `.dockerignore` нужен именно рядом с `Dockerfile`: образ собирается с `context: ./docs-site`,
  корневой на этот контекст не действует. Без него `COPY . .` клал локальное `node_modules`
  поверх установленного в образе — с бинарниками под чужую платформу и виндовыми шимами в `.bin`
  (отсюда и заметка в `Dockerfile` про exec-бит и `EXIT 126`).
- В git лежат только исходники: `.vitepress/config.mts`, `.vitepress/theme/` и markdown-страницы.
  `docs-site/Dockerfile` собирает сайт сам, поэтому **правки конфига доезжают до прода только
  пересборкой контейнера `docs`**.

### Поток `POST /v1/chat/completions`

0. Rate limit (`GatewayRateLimiting`, политика `gateway-chat`): fixed-window per-API-key
   с фолбэком per-IP, секция `RateLimiting`, при `Enabled=false` — no-op. Отказ — 429
   в OpenAI-формате ошибки + `Retry-After`.
1. Bearer-токен (`sk-or-*`) → валидация в AuthService (UserId, ApiKeyId, IsAdmin).
2. Баланс, spend-лимиты ключа (суточный/недельный) и доступность модели в BillingService.
   Здесь публичный `ModelId` (`ORR/GLM-4.7`) резолвится во внутренний `RouterModelId` (`glm-4.7`).
   Billing отдаёт и цены модели; gateway (`SpendBudgetGuard`, T-070) по ним оценивает вход:
   не покрывается остатком → 402, иначе урезает `max_tokens` под остаток.
   **Инвариант денег (T-070):** списание usage зажато остатком и сериализовано `for update` —
   баланс не уходит в минус и не «застревает» на копейках. Раньше all-or-nothing-списание
   оставляло остаток, который проходил pre-check `balance > 0`, и запросы шли бесплатно
   бесконечно (инцидент 2026-09-28).
3. Выбор ноды в ControlPlane — **передаётся `RouterModelId`**, иначе capability-store
   не сматчит модель. Capability-aware, при пустом store — fallback на least-load.
4. Патч тела: `model` заменяется на `RouterModelId`. Если id модели содержит "gemini" —
   `GeminiSchemaPatcher` инлайнит `$ref`/`$defs` в схемах тулов (Gemini их не принимает).
5. Проксирование: YARP (non-streaming) либо `HttpCompletionOption.ResponseHeadersRead` (streaming).
   Ретраи на другие ноды при 5xx/сетевой ошибке — цикл до `GatewayRetry:MaxRouterAttempts`
   (дефолт 5), только пока первый байт не ушёл клиенту.
6. Usage вынимается из JSON-тела или из финального SSE-чанка (`include_usage`).
7. Событие usage кладётся в **PostgreSQL-outbox** (`db/migrations/gateway/`,
   `PostgresUsageOutboxRepository` + `UsageOutboxDispatcher`), лиза освобождается.

### Эндпоинты gateway и что из них биллится

`MapGatewayEndpoints` (`Application/Extensions/ApiGatewayExtensions.cs`) вешает пять
проксирующих POST-а (все под rate-limit-политикой `gateway-chat`) и четыре GET-а:

| Эндпоинт | Handler | Usage / биллинг |
|---|---|---|
| `POST /v1/chat/completions` | `ChatCompletionHandler.HandleAsync` | outbox (токены из тела) |
| `POST /v1/chat/completions/stream` | `ChatCompletionHandler.HandleStreamAsync` | outbox (токены из финального SSE-чанка) |
| `POST /v1/messages` | `MessagesHandler.HandleAsync` (Claude Code / Anthropic Messages) | outbox (токены из ответа / Anthropic SSE stream, с поддержкой кэша и `_orr_raw`) |
| `POST /v1/audio/speech` | `TextToSpeechHandler` | outbox; **`output_tokens` = число Unicode-символов `input`** |
| `POST /v1/images/generations` | `ImageGenerationHandler` | **никакого usage не пишется — запросы не биллятся** |
| `GET /v1/models`, `/health`, `/ready` | `ModelsHandler` / инлайн | — |
| `GET /robots.txt` | инлайн `User-agent: *` / `Disallow: /` | — |

Все ответы API получают `X-Robots-Tag: noindex, nofollow`; это не меняет тела, коды или streaming-контракт.

Все проксирующие POST-ы идут через `GatewayPipeline`
и один и тот же retry/failover-цикл по нодам (`MaxRouterAttempts = 5` в image/tts/messages-хендлерах).

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
- **gRPC-инфраструктура**: в `BuildingBlocks/Grpc/` лежат `GrpcDeadlineInterceptor`
  (`GrpcServices:CallDeadlineSeconds`, дефолт 30) на всех клиентах, клиентская половина
  межсервисной аутентификации `ServiceAuthClientInterceptor` и серверный
  `ServiceAuthInterceptor` с `ServiceAuthOptions`/`ServiceAuthServerOptions`. Каждый сервис
  конфигурирует свой набор защищённых методов через `ServiceAuthServerOptions.ProtectedMethods`
  в DI. На серверах ApiGateway, AuthService, ControlPlane и CollectorService s2s-проверки нет.
  Structured gRPC error details пока нет (PLATFORM-002).
- **Correlation ID**: заголовок `X-Correlation-Id` → `CorrelationIdReader` → контекст Serilog.
- **Конфигурация gRPC-адресов**: секция `GrpcServices` в `appsettings.json` потребителя
  (`Auth` → `http://localhost:5001`, `Billing` → `:5002`, `ControlPlane` → `:5003`,
  `Collector` → `:5005`, `Content` → `:5007`).
- **Порт 5006 не занимать**: в compose (`deploy/compose/compose.yml`, `compose.prod.yml`) это
  HTTP-эндпоинт коллектора (`5006:8080`), по нему же ходит health-чек в `update-orr.sh`.
  Новым сервисам — следующий свободный порт.

### Карта страниц WebApp

**Кебаб-кейса в маршрутах нет.** Страницы без явного `@page "/..."` получают путь из имени
файла как есть, в нижнем регистре: `Workspace/ApiKeys.cshtml` → `/workspace/apikeys`, а не
`/workspace/api-keys`. Не угадывай путь — проверяй по `@page` или по имени `.cshtml`.

Публичные: `/` (лендинг), `/login`, `/signup` → `/verify-email`, `/forgot-password` →
`/reset-password`, `/invite/{token}`, `/models`, `/models/{provider}/{slug}`, `/providers`,
`/providers/{id}`, `/tools`, `/tools/{id}`, `/docs`, `/support`, `/legal/offer`,
`/legal/privacy`, `/pay/mock/{paymentId}`, `/logout`,
`/login/externalcallback` (возврат из Google/Yandex OAuth).

Кабинет: `/workspace` (редирект на `/workspace/apikeys`), `/workspace/apikeys`,
`/workspace/apikeys/limits`, `/workspace/activity`, `/workspace/credits`,
`/workspace/paymentsuccess`, `/workspace/settings`.

Админка: `/admin`, `/admin/users`, `/admin/users/{userId}/edit`, `/admin/topups`, `/admin/models`,
`/admin/models/sync`, `/admin/models/prices`, `/admin/tags`, `/admin/providers`, `/admin/tools`, `/admin/tools/edit/{id?}`,
`/admin/nodes`, `/admin/nodes/{nodeId}`, `/admin/nodes/{nodeId}/edit`,
`/admin/nodes/{nodeId}/provider-limits`, `/admin/anomalies`, `/admin/requests`, `/admin/broadcast`,
`/admin/gatewaytest`.

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
- **Провайдеры** за `IPaymentProvider` + `PaymentProviderRegistry`: `mock` (только dev),
  `rollypay` (СБП-агрегатор, `Services/Payments/Rollypay/`) и
  `yandexpay` (sandbox, карта, `Services/Payments/YandexPay/`). Активный публичный провайдер
  выбирается `Payments:ActiveProvider`; пустое значение просто выключает публичные платежи.
  Yandex Pay используется только через admin-only эндпоинт чекаута и не зависит от `ActiveProvider`.
- **`PaymentStartupGuard`** падает на старте, если вне Development активен `mock` или
  неизвестный провайдер. Не ослаблять — мок не должен быть доступен в проде.
- **Вебхук** — `PaymentWebhookEndpoint.Map(app)`: анонимный (провайдер не может залогиниться),
  per-IP rate limit, лимит размера тела, **verify-then-parse по сырому телу**
  (`RollypayWebhookVerifier`). Незаконфигуренный секрет → 401, а не пропуск.
  Yandex Pay вебхук (`POST /v1/webhook`) — JWT ES256, верификация через JWK-ключи
  (`YandexPayWebhookVerifier`), `merchantId` валидация, сумма для `CAPTURED` подтягивается
  из Merchant API.
- **`PaymentReconciliationWorker`** добирает статусы: `Created` без оплаты уходит в `Expired`.

Страницы: `/workspace/credits` (пополнение), `/pay/mock/{paymentId}` (dev-заглушка оплаты),
`/workspace/paymentsuccess`, `/admin/topups` (история пополнений по всем пользователям).

## Лимиты, метрики, продуктовая аналитика

- **Офлайн-конверсии Яндекс Метрики** используют разделённое хранение: WebApp после согласия
  сохраняет связь `user_id → _ym_uid` в `analytics.yandex_metrika_identities`, а BillingService
  атомарно пишет `topup_completed` и первый оплачиваемый `first_api_request` в
  `billing.yandex_metrika_offline_conversion_outbox`. Воркер BillingService подставляет ClientID,
  отправляет CSV в Management API и сверяет статус загрузки; отсутствие ClientID не теряет событие.
  Удаление продуктовой аналитики пользователя обязано удалить и эту связь.

- **`LimitMetrics`** (`BuildingBlocks/Metrics/`) — единый счётчик отказов по лимитам,
  в Prometheus как `orr_limit_rejections_total`. Метки узкие (`limit_type` × `subject`,
  ~5 серий). **В лейблы нельзя класть api key id / user id / ip** — неограниченная
  кардинальность на публичном эндпоинте. Разрезы «кто именно упёрся» берём SQL-панелями
  поверх `billing.usage_records`, не метриками.
- **Продуктовая аналитика** (`BuildingBlocks/Analytics/`): очередь + фоновой флаш
  (`ProductAnalyticsEventQueue` → `ProductAnalyticsFlushWorker`) в `analytics.events`.
  Есть no-op реализация, псевдонимизация пользователя (`UserPseudonymizer`) и путь стирания
  (`IProductAnalyticsErasure`). Схема — `db/migrations/analytics/`.
- **Трафика сайта система не считает сама** — это делает Яндекс.Метрика (счётчик в layout WebApp,
  секция `Analytics`). Собственный контур (`analytics.site_visits`/`visit_sessions`,
  `SiteVisitTrackingMiddleware`, компакт в BillingService, страницы `/admin/visitors`
  и `/admin/sessions`, секция «Посетители сайта» в дашборде `ORR Users`) **удалён**, обе таблицы
  дропает миграция `db/migrations/analytics/004_drop_site_visits.sql`. Не реинтродьюсить:
  дублировать Метрику своим трекером незачем. `analytics.events` (продуктовая аналитика) —
  отдельный, живой конвейер.
- **Метрики нод и провайдеров пишет CollectorService** (`ProviderQuotaMeter`) — остатки квот
  (`orr_provider_quota*`), потолок окна и момент его обнуления (`orr_provider_quota_limit`,
  `orr_provider_quota_reset_seconds`; **длину окна — «5-часовой», «недельный» — не сообщает
  ни один провайдер**, она оценивается по горизонту до сброса и на дашборде помечена «~»),
  их читаемость (`orr_provider_quota_status` + `reason`) и полный срез
  «что нода отдаёт на подключении» — `orr_node_model_catalog{node_id, provider, connection_id,
  model_id, router_model_id, billing_state}`, где `billing_state` ∈ `enabled|disabled|absent`
  (закрытый список `ModelBillingStates`). Это единственный источник «моделей, которых нет
  в каталоге ORR»: в БД связки нода↔подключение↔модель не существует вообще, она живёт только
  в in-memory снапшоте коллектора и в этих метриках.
- Дашборды Grafana и конфиг Prometheus для прода — в `deploy/compose/grafana/`
  и `deploy/compose/prometheus.prod.yml`. Дашборды: `ORR Limits` (лимиты и квоты),
  `ORR Product Analytics` (псевдонимные события), `ORR Users` (рост, retention; строится по
  `auth`+`billing`, трафик сайта смотрим в Яндекс.Метрике), `ORR Nodes & Provider Limits`
  (`nodes-providers.json` — список нода → подключение провайдера → квоты и модели).
- **В `ORR Nodes & Provider Limits` селектор `$node` фильтрует только детальный список.**
  Переменная — query по `control.router_nodes` (`__text` = имя, `__value` = id; колонка
  `character varying` без дефисов, поэтому значение совпадает с меткой `node_id` в Prometheus),
  `allValue = .*` под фильтр `=~"$node"`. Сводка по провайдерам намеренно остаётся по всему
  парку нод — ради этого у большой панели есть **отдельный нефильтрованный таргет `E`**
  (`orr_provider_quota_percent` без селектора). Уберёшь `E` или навесишь на него `$node` —
  сводка молча схлопнется до одной ноды.
- **Панель со смешанными датасорсами обязана быть `-- Mixed --`.** У большой панели
  `ORR Nodes & Provider Limits` четыре таргета в Prometheus и один (`D`) в Postgres.
  Grafana учитывает `datasource` **отдельного таргета** только если у панели на верхнем уровне
  стоит `{"type":"datasource","uid":"-- Mixed --"}`; с конкретным датасорсом все таргеты уходят
  в него, и Prometheus молча выбрасывает SQL-таргет (у него нет `expr`). Симптом — не ошибка,
  а исчезнувшие данные: в заголовках нод вместо имён сырые `node_id`, без плашек статуса
  и in-flight. Единственная такая панель в проекте; UI при этом честно рисует «D (ORR Billing)»,
  так что по редактору проблему не видно.
- **`ORR Nodes & Provider Limits` требует плагина** `marcusolsson-dynamictext-panel` 5.5.0
  (Business Text; 6.x уже требует Grafana 11, а в compose стоит 10.4.2). Ставится через
  `GF_INSTALL_PLUGINS` при старте контейнера, разметку строит JS в `options.afterRender`
  панели — поэтому там же включён `GF_PANELS_DISABLE_SANITIZE_HTML`. Верхняя `bargauge`-панель
  нативная и остаётся рабочей, даже если плагин не установился.
- **В `GF_INSTALL_PLUGINS` версия отделяется пробелом, не `@`**: `"<id> <version>"`.
  Grafana'вский `/run.sh` подставляет запись в `grafana cli plugins install` без кавычек, так
  что пробел даёт второй аргумент, а `id@version` уходит одним id и ловит `404: Plugin not
  found`. Entrypoint — `#!/bin/bash -e`, поэтому такой 404 роняет контейнер в CrashLoopBackOff,
  а не просто пропускает плагин.
- **В k8s-оверлее те же дашборды раздаёт `configMapGenerator grafana-dashboards-orr`**
  (`infra/kustomize/base/infra/kustomization.yaml`, файлы в `base/infra/dashboards/` — это
  **копии** `deploy/compose/grafana/dashboards/`, kustomize не умеет читать выше своей
  директории; правишь дашборд — правь обе копии). Для `ORR Nodes & Provider Limits` там же
  включены `GF_INSTALL_PLUGINS`/`GF_PANELS_DISABLE_SANITIZE_HTML` (`base/infra/grafana.yaml`),
  а под скачивание плагина grafana добавлена в `allow-external-egress` — иначе под
  крэшлупит на старте.
- **Панель нод берёт `control.router_nodes` из датасорса `ORR Billing`.** В compose auth,
  billing и control лежат в одной БД, в кластере они разнесены, поэтому `db-create`-job
  (`migration-job.yaml`) прокидывает в `orr_billing` **два** foreign schema через
  `postgres_fdw`: `auth` (панели ORR Users) и `control` (эта панель). Если сервис-владелец
  ещё не накатил миграции, job пропускает **только его** схему и следующий синк Argo
  повторяет.
- **Алерты Grafana уходят почтой не по SMTP.** На прод-хосте закрыт исходящий SMTP
  (25/465/587/2525), открыт только 443, поэтому native SMTP contact point не работает.
  Схема: правила `deploy/compose/grafana/provisioning/alerting/{rules,policies,contactpoints}.yaml`
  → webhook `POST http://webapp:8080/internal/alerts/grafana` (`AlertWebhookEndpoint`)
  → общая очередь писем WebApp → Brevo HTTP API (`Email:Transport=brevoApi`, ключ в
  `Email:BrevoApiKey`). Эндпоинт анонимный, но гейтится shared-токеном
  (`Alerting:WebhookToken`, приходит в `Authorization: Bearer` либо `X-Alert-Token`,
  сравнение constant-time); без `Alerting:WebhookToken`/`Alerting:EmailTo` он отдаёт 503,
  а не остаётся открытым. Payload парсится защитно: сломанный JSON уезжает письмом «как есть»,
  алерт не теряется.

## Два типа токенов 9Router — не путать

1. **Control Token** (на ноду). Лежит в `control.router_nodes.control_token` как
   **само значение токена**, а не имя секрета. Уходит как есть в
   `Authorization: Bearer <control_token>` к admin API ноды (`/api/integrations/*`).
   Никакого резолвинга через конфиг в `RouterNodeProvider` нет — и не надо
   реинтродьюсить `RouterHealthChecks:NodeTokens:{name}` (это было ошибкой в старой доке).
2. **Пользовательские API-ключи** (`sk-or-*`). Создаются в WebApp, хеш — в `auth.api_keys`,
   при создании пушатся на активные ноды, при отзыве деактивируются. Маппинг
   `(api_key_id, node_id, router_key_id)` — в `control.integration_key_sync`.

## Telegram-бот (`tg_bot/`)

По решению владельца production-исходники отдельного бота включены в этот репозиторий.
TypeScript/Node.js 24/Fastify, отдельный Compose-проект `rawrter-tg-bot`, не .NET-библиотека.
Перед правками читать `tg_bot/AGENTS.md` и `tg_bot/README.md`; обязательны npm typecheck/test/build.
Бот принимает Support/registration/topup/first-api-request/payment-created и получает суточную сводку через
закрытый HTTP API WebApp. Команд Telegram/топиков пока нет. Отдельную БД не создавать;
будущий доступ к существующей БД — только чтение, текущая очередь остаётся in-memory.

Workflow `tg-bot.yml` проверяет/публикует образ, но не деплоит. Обновлённый `update-prod.sh`
после health-gate основных сервисов вызывает `update-tg-bot.sh`, который собирает бот из
`app/tg_bot`, сохраняет прежний Compose project и env-file `/opt/rawrter-tg-bot/.env`.
При неудаче бота откатывается только его image; основной сервис остаётся обновлённым.
Старую копию `/opt/orr/update-prod.sh` нужно один раз установить вручную после разрешения
владельца (инструкция в README бота); Git pull не заменяет эту копию автоматически.
Legacy `deploy-prod.yml`/`update-orr.sh` не обновляет бот. Внешняя проверка миграции ожидается.

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

### SSH-деплой на прод (201.51.6.246)

Прод-сервер: `201.51.6.246`, пользователь `root`, репо в `/opt/orr`.

**SSH-ключ** (без пароля): `~/.ssh/id_ed25519_opencode` (ed25519, fingerprint `SHA256:amHOzogynksICpE1pFQfJeu5wsDAk70e4uz3V9ghcBw`).
Ключ уже в `~/.ssh/authorized_keys` на сервере. ssh.exe использует его через `-i`:

```powershell
ssh -i "$env:USERPROFILE\.ssh\id_ed25519_opencode" -o StrictHostKeyChecking=yes -o PasswordAuthentication=no root@201.51.6.246 "<команда>"
```

**Деплой всех сервисов** — `update-prod.sh` (git pull → sync compose → build → restart → health-gate → rollback при ошибке):

```powershell
ssh -i "$env:USERPROFILE\.ssh\id_ed25519_opencode" -o StrictHostKeyChecking=yes -o PasswordAuthentication=no root@201.51.6.246 "cd /opt/orr && bash update-prod.sh"
```

**Выката одного сервиса нет:** `update-prod.sh` игнорирует аргументы. Он всегда пересобирает все
.NET-сервисы, перезаливает `compose.yml`/`Caddyfile`/`prometheus.yml`/`grafana/` из
`app/deploy/compose`, рестартит caddy/prometheus/grafana и обновляет tg-бота.

**Перед выкатом** проверь, что клон `/opt/orr/app` чистый (`git -C /opt/orr/app status --short`).
Любая правка, сделанная прямо на сервере, роняет `git pull --ff-only`. Такие правки переносим в git,
вслепую не сносим (2026-09-29 так нашлись хотфикс gateway и миграция 032, жившие только на проде).

**Workflow `deploy-prod.yml` живой прод не обновляет:** секрет `PROD_SSH_HOST` указывает на другой
хост, который с 2026-08-05 стоит на `1f8a35a6` с локальными правками `compose.yml`, и каждый
запуск падает на `git pull`.

**Для подключения через plink** (PuTTY):

```powershell
& "C:\Program Files\PuTTY\plink.exe" -hostkey "ssh-ed25519 255 SHA256:0wUJ9GDwPh1tByk1H5m0o3sy2owKN2ZY7qOn7PBL5YA" -i "$env:USERPROFILE\.ssh\id_ed25519_opencode" root@201.51.6.246 "<команда>"
```

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
| ControlPlane | `in_flight_requests` персистится и реплеится при загрузке ноды, но сверки с реальными лизами нет: счётчик дрейфует вверх и рестартом не лечится (CONTROL-004) |
| ApiGateway | `/v1/images/generations` проксируется, но usage не пишет — картинки не биллятся |
| ApiGateway | Констрейнт `ck_usage_outbox_status` не знает статус `usage_missing` — запросы с нулевым usage не доезжают до биллинга, ошибка глушится `try/catch` (GATEWAY-001) |
