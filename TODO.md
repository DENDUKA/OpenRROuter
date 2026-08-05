# OpenRussiaRouter: подробный TODO по сервисам

## Цель документа

Этот документ превращает PRD в технический backlog для MVP. Каждая задача описывает постановку, важные технические особенности и минимальные тесты. Формат рассчитан на перенос в YouTrack/Jira/GitHub Issues.

## Статус выполнения

### Закрыто

- `[x] PLATFORM-001` - создана структура solution, сервисные проекты, shared contracts/building blocks и test projects. `dotnet build OpenRussiaRouter.sln` проходит.

### Частично закрыто

- `[~] PLATFORM-002` - добавлены shared `ApiError` и `ErrorCodes`, но еще не реализованы gRPC interceptors/error details и contract tests маппинга.
- `[~] PLATFORM-003` - добавлены health/readiness endpoints и correlation id primitive, но еще нет полноценного logging/metrics/tracing pipeline и log redaction tests.
- `[~] AUTH-001` - реализованы доменные модели `UserAccount`, `Invite`, роли, статусы, одноразовый invite token, PostgreSQL persistence entities/mapping, SQL migration `db/migrations/auth/001_initial.sql`, Dapper repository и persisted invite completion flow. Еще нужны admin endpoints/gRPC methods и invite delivery/display flow.
- `[~] AUTH-002` - реализован `LoginService`: проверка email/password credential, disabled user, выдача session token и unit-тесты. Добавлен gRPC `Login` метод. Реализованы Blazor login/invite screens, session persistence via ProtectedLocalStorage и role-based navigation. Еще нужны authorization policies и cookie-based auth.
- `[~] AUTH-003` - реализованы доменная модель `ApiKey`, secure raw key generation, SHA-256 hash storage, revoke/rename/use flow, PostgreSQL schema/mapping, SQL migration, Dapper-backed `ValidateApiKey` с `lastUsedAt` update и Dapper repository для create/revoke/rename/list. Еще нужны user-facing API/gRPC methods.
- `[~] GATEWAY-005` - Gateway подключен к Auth `ValidateApiKey` по gRPC, Bearer parsing вынесен в отдельный компонент, `/v1/models` и `/v1/chat/completions` теперь требуют валидный Auth ответ. Еще нужны integration tests с настоящим gRPC test server, кеширование validation по необходимости и связка с persisted API keys.
- `[~] GATEWAY-001` - `/v1/models` теперь получает список моделей из Billing `ListPublicModels` по gRPC и возвращает OpenAI-compatible list shape. Еще нужны integration tests и реальный persisted model catalog.
- `[~] GATEWAY-002` - `/v1/chat/completions` теперь извлекает `model`, проверяет Auth и Billing `CheckBalanceAndModel`, выбирает 9Router-ноду через Control Plane, проксирует non-streaming запрос в HTTP adapter, возвращает upstream response и пишет `RecordUsage` при наличии usage payload. Еще нужны integration tests через test server, streaming path и контракт с реальным 9Router.
- `[~] GATEWAY-004` - при сетевой ошибке или 5xx Gateway освобождает lease со статусом `failed` и один раз пробует другую healthy-ноду для safe non-streaming. Еще нужен SSE до-first-chunk retry path.
- `[~] GATEWAY-006` - public errors возвращаются в OpenAI-compatible shape с `type`, `code`, `message`, `request_id`, `X-Request-Id` и correlation header; internal error не раскрывает details. Еще нужны endpoint-level integration tests для всех Gateway paths.
- `[~] BILLING-001` - реализован append-only ledger, virtual top-up, баланс из ledger, `IBillingStore`, in-memory и Dapper/PostgreSQL store с idempotency key handling, SQL migration, Testcontainers PostgreSQL integration test и gRPC admin method. Еще нужен UI method.
- `[~] BILLING-002` - реализована проверка `balance > 0 + enabled model` в application layer с тестами. Еще нужен gRPC endpoint `CheckBalanceAndModel` и интеграция Gateway.
- `[~] BILLING-003` - реализован model/pricing catalog, enabled/disabled фильтр, price version в usage record, Dapper/PostgreSQL persistence model и admin gRPC upsert method. Еще нужны UI methods и immutable price history вместо одного active row.
- `[~] BILLING-004` - реализован прием usage event в application layer, идемпотентность по requestId, charge ledger entry, anomalies для invalid tokens / missing model price и Dapper/PostgreSQL store для usage/anomalies. Еще нужны PostgreSQL-backed outbox и integration tests на реальной PG.
- `[~] BILLING-005` - реализован shared `UsageCostCalculator` и подключен в Billing/Usage application service. Еще нужны persisted price snapshots и UI breakdown.
- `[~] CONTROL-001` - реализован registry внешних 9Router-нodi, HTTPS endpoint validation, enable/disable, `IControlPlaneStore`, in-memory и Dapper/PostgreSQL store, SQL migration, Testcontainers PostgreSQL integration test и admin gRPC methods. Еще нужны UI methods.
- `[~] CONTROL-002` - реализовано применение health status в application layer, исключение disabled nodes из health/routing, HTTP health-check client с node token, background worker и Dapper/PostgreSQL поля health status/latency/error. Еще нужны cooldown/flapping policy и сохранение capabilities/version.
- `[~] CONTROL-003` - реализован least-load выбор healthy enabled ноды по normalized in-flight/capacity, gRPC `SelectRouterNode` и тесты. Еще нужны capability/model-aware routing и persisted metrics.
- `[~] CONTROL-004` - реализованы lease/release, TTL cleanup, защита счетчика in-flight от ухода ниже нуля, background cleanup worker и Dapper/PostgreSQL persisted leases. Еще нужны recovery policy после рестарта сервиса и integration tests на реальной PG.

### Закрыто после старта документа

- `[x] AUTH-004` - подключен gRPC hosting в Auth Service, добавлены `ValidateApiKey`, `CreateInvite`, `CompleteInvite`, `CreateApiKey`, `RevokeApiKey`, `Login`, `auth.proto` csharp namespace, in-memory validator/repository для local mode, Dapper-backed validator/repository при наличии `ConnectionStrings:AuthDb`, SQL contract tests, opt-in PostgreSQL integration tests и gRPC test-server integration tests.
- `[x] AUTH-PG-TESTCONTAINERS` - Auth PostgreSQL persistence tests переведены с env-only connection string на Testcontainers PostgreSQL runner с safe no-op при недоступном Docker.
- `[x] BILLING-PG-TESTCONTAINERS` - Billing PostgreSQL persistence tests переведены с env-only connection string на Testcontainers PostgreSQL runner с safe no-op при недоступном Docker.
- `[x] CONTROL-PG-TESTCONTAINERS` - Control Plane PostgreSQL persistence tests переведены с env-only connection string на Testcontainers PostgreSQL runner с safe no-op при недоступном Docker.
- `[x] BILLING-007` - подключен gRPC hosting в Billing Service, добавлены endpoints `CheckBalanceAndModel`, `RecordUsage`, `ListPublicModels`, `GetBalance`, `CreateVirtualTopUp`, `UpsertModelPrice`, `ListUsage`, `ListBillingAnomalies`, runtime switch in-memory/Dapper по `ConnectionStrings:BillingDb`, SQL contract tests, Testcontainers PostgreSQL integration test, gRPC test-server integration test и gRPC test-server + PostgreSQL integration test.
- `[x] CONTROL-005` - подключен gRPC hosting в Control Plane, реализованы `SelectRouterNode`, `ReleaseRouterNode`, `RegisterRouterNode`, `DisableRouterNode`, `ListRouterNodes`, runtime switch in-memory/Dapper по `ConnectionStrings:ControlPlaneDb`, SQL contract tests, Testcontainers PostgreSQL integration test, gRPC test-server integration test и gRPC test-server + PostgreSQL integration test.
- `[x] UI-001` - реализованы Blazor login screen (`/login`), invite completion screen (`/invite/{token}`), logout, auth state management (`AuthState`), session storage (`SessionStorage`), role-based navigation в NavMenu, User Cabinet (`/cabinet`) и Admin Dashboard (`/admin`). Добавлен gRPC `Login` метод в auth.proto и GrpcAuthService.
- `[x] UI-002` - реализована страница API keys (`/cabinet/keys`): создание ключа (raw показывается один раз), список, переименование, отзыв. Добавлены gRPC методы `ListApiKeys` и `RenameApiKey`.
- `[x] UI-003` - реализована страница usage (`/cabinet/usage`): баланс, таблица usage records с токенами и стоимостью. Добавлен `ListUsage` с user_id фильтром.
- `[x] UI-004` - реализована admin страница users (`/admin/users`): список пользователей, создание invite, virtual top-up. Добавлены gRPC методы `ListUsers` и `CreateInvite`.
- `[x] UI-005` - частично реализована admin model/pricing management. Добавлены `ListPublicModels` в BillingClient.
- `[x] UI-006` - реализована admin страница router nodes (`/admin/nodes`): список нод, добавление, отключение. Добавлены gRPC методы `ListRouterNodes` и `DisableRouterNode`.
- `[x] UI-007` - реализована admin страница anomalies (`/admin/anomalies`): список billing anomalies.

### Следующий фокус

- Gateway streaming/retry integration без развертывания 9Router: SSE proxy contract, safe retry и test fake upstream.

## Трафик сайта: сессии (известные пробелы)

С VS-001..VS-010 `analytics.site_visits` стала эфемерным буфером: `VisitSessionCompactionWorker`
(BuildingBlocks, хостит BillingService) схлопывает визиты в `analytics.visit_sessions` и удаляет
исходные строки, поэтому в таблице визитов лежит `SessionGap + SafetyLag` ≈ 35 минут трафика.
Что осталось незакрытым:

- `[ ] VISITS-001` - **у `analytics.visit_sessions` нет retention.** Компакт только переносит
  строки: он ограничивает рост `site_visits`, но таблица сессий растёт бесконечно (примерно в 4
  раза медленнее по строкам, чем росли бы визиты, плюс `pages jsonb` на сессию). Нужна политика
  удаления/агрегации старых сессий — партиционирование по `started_at_utc` либо периодический
  `delete` с окном; ни того, ни другого сейчас нет, и никакого предупреждения о размере тоже.
- `[ ] VISITS-002` - **`IProductAnalyticsErasure.PurgeUserAsync` не чистит `visit_sessions`.**
  Метод удаляет только `analytics.events` по вычисленному `distinct_id`. Это **пре-существующий**
  пробел, а не регрессия компакта: `site_visits` он не чистил и до VS-001 — просто теперь тот же
  `distinct_id` вместе с `visitor_hash` и путями переживает стирание сколько угодно долго, а не
  ~35 минут. В объём VS-001..VS-010 работа не входила. При закрытии учесть, что удалять надо по
  `distinct_id` **и** по связанному `visitor_hash` (анонимный хвост до регистрации), иначе журнал
  остаётся привязываемым к пользователю через `visit_sessions`.
- `[ ] VISITS-003` - **известное искажение сессий при позднем флаше.** Визит, дошедший до БД с
  задержкой больше `SafetyLagMinutes` (переполненная очередь, тормозящая запись, перекос часов
  реплики WebApp — `occurred_at_utc` ставит приложение, а не `default now()`), попадает в уже
  схлопнутую сессию. Если его `started_at_utc` совпал с существующей строкой, `on conflict do
  update` домержит просмотр корректно; если не совпал — появится **сессия-фрагмент из одного
  просмотра**, и «уникальные посетители» не поедут, а «сессий за день» слегка завысится. Лечение —
  не уменьшать `SafetyLag`; полное устранение потребовало бы окна перезаписи уже закрытых сессий.
  Стоит в том же ряду, что и остальные оговорки трекинга: посетители за одним NAT склеиваются в
  одного (`visitor_hash` = `HMAC(salt, ip + user-agent)`), а `X-Forwarded-For` управляется
  клиентом, поэтому счётчик посетителей можно накрутить.

## Yandex Pay: известные пробелы

Интеграция сделана по механике «платёжная ссылка без SDK» (`POST /api/merchant/v1/orders` →
`paymentUrl` → редирект), это допустимый вариант по
[гайду](https://pay.yandex.ru/docs/ru/custom/integration-guide-link). Вебхук и юр. документы
приведены в соответствие; осталось:

- `[ ] YAPAY-001` - **фискализация по 54-ФЗ не реализована.** По условиям партнёра чек покупателю
  выдаёт продавец, не позже дня получения подтверждения оплаты; Яндекс чеки не выдаёт (кроме
  комиссии по длинному Сплиту). Нет ни интеграции с кассой (АТОЛ Онлайн / CloudKassir), ни поля
  `receipt` в запросе `/orders`. Вместе с этим придётся пересмотреть п. 6.7 публичной оферты
  («документом, подтверждающим оплату, является запись о платеже в учётной системе»).
- `[ ] YAPAY-002` - **флаг `Payments:YandexPay:Sandbox` мёртвый — на прод переключиться нельзя.**
  `YandexPayClient.SandboxBaseUrl` и `YandexPayJwkProvider.SandboxJwksUrl` захардкожены, а API-ключ
  берётся из `MerchantId`; в боевой среде хост `https://pay.yandex.ru` и ключ отдельный от
  merchant id. Нужен `ApiKey` в `YandexPaySettings` и выбор хоста по флагу. Заодно проверить, не
  требуется ли `merchantId` в теле `/orders` — в гайде он в списке полей, мы его не шлём (в
  sandbox проходит, потому что ключ = merchant id).
- `[ ] YAPAY-003` - **кнопка admin-only.** Чекаут гейтится `IsAdmin && YandexPayEnabled`, и
  Yandex Pay не участвует в `Payments:ActiveProvider` — обычным пользователям способ недоступен.
- `[ ] YAPAY-004` - **реквизиты Оператора не заполнены.** `Legal:ORR_OPERATOR_NAME` /
  `ORR_OPERATOR_DETAILS` / `ORR_SUPPORT_CONTACT` в проде остаются на дефолтах (только домен),
  поэтому п. 15.4 оферты не содержит юрлица/ИП, ИНН и адреса. Задать через env в
  compose/kustomize перед подключением merchant'а.

## Общие правила реализации

- Основной стек: .NET 8/9, C#, ASP.NET Core, gRPC, PostgreSQL, Kubernetes.
- Внутренние синхронные вызовы между .NET-сервисами идут через gRPC.
- Публичный API остается HTTP/OpenAI-compatible.
- Для тестов использовать отдельные test projects по шаблону `[ProjectName].Tests`.
- Unit-тесты писать на xUnit по AAA-паттерну; для интеграционных тестов использовать Testcontainers/PostgreSQL и in-memory/mock gRPC clients там, где это уместно.
- Каждый сервис владеет своей базой данных и миграциями. Прямые запросы в чужую базу запрещены.
- Prompt/response bodies не сохраняются в persistent storage и обычные логи.
- Все money-значения хранить в минимальных денежных единицах или decimal с явной валютой $. Не использовать `double` для денег.
- Все token/cost операции должны быть идемпотентны по request/usage id.

## 0. Shared Contracts / Platform Foundation

### PLATFORM-001: Создать структуру solution и базовые проекты

**Постановка:** создать .NET solution с отдельными проектами для API Gateway, Auth Service, Billing/Usage Service, Router Control Plane, Blazor UI, shared contracts и тестов.

**Технические особенности:**
- Разделить сервисы на самостоятельные deployable projects.
- Вынести `.proto` контракты в shared contracts project или отдельный пакет.
- Подготовить общие соглашения: correlation id, request id, error codes, pagination, timestamps in UTC.
- Настроить nullable reference types, analyzers, TreatWarningsAsErrors для production projects.

**Тесты:**
- `dotnet build` проходит на пустом skeleton.
- Test projects подключены и запускаются через `dotnet test`.
- Проверить, что public service projects не зависят напрямую от чужих infrastructure/data projects.

### PLATFORM-002: Описать gRPC error model

**Постановка:** определить единый формат внутренних ошибок для gRPC-вызовов: authentication, authorization, insufficient balance, model unavailable, router unavailable, validation error, internal error.

**Технические особенности:**
- Использовать gRPC status codes плюс structured error details.
- Не прокидывать внутренние exception messages в публичный API.
- Gateway должен маппить внутренние ошибки в OpenAI-compatible HTTP error shape.

**Тесты:**
- Unit-тесты маппинга gRPC errors в HTTP status/error body.
- Проверка, что sensitive details не попадают в публичный ответ.
- Проверка, что correlation id сохраняется в логах и response headers.

### PLATFORM-003: Настроить observability primitives

**Постановка:** заложить общие logging/metrics/tracing conventions для всех сервисов.

**Технические особенности:**
- Structured logging: service, environment, correlationId, accountId, apiKeyId, modelId, routerNodeId.
- Не логировать Authorization headers, API key raw value, prompt, messages, completion text.
- Подготовить health endpoints: `/healthz`, `/ready`.
- Метрики: request count, latency, error count, gRPC dependency latency, PostgreSQL latency.

**Тесты:**
- Unit-тесты log redaction helper.
- Интеграционный тест health/readiness endpoints.
- Проверка, что mock prompt body не появляется в captured logs.

## 1. API Gateway Service

### GATEWAY-001: Реализовать OpenAI-compatible `/v1/models`

**Постановка:** endpoint должен возвращать только модели, разрешенные .NET-слоем, в формате, совместимом с OpenAI clients.

**Технические особенности:**
- Gateway получает список публичных моделей через Billing/Usage или отдельный catalog gRPC method, если каталог находится в Billing.
- Модель должна иметь public model id, display name, enabled flag, optional metadata.
- Не показывать upstream/private identifiers 9Router.
- Требуется API key authentication.

**Тесты:**
- Валидный API-ключ получает список только enabled моделей.
- Невалидный/отсутствующий ключ получает 401.
- Disabled model не попадает в ответ.
- Response shape совместим с OpenAI `/v1/models`.

### GATEWAY-002: Реализовать OpenAI-compatible `/v1/chat/completions` non-streaming

**Постановка:** endpoint принимает OpenAI-compatible chat completion request, проверяет ключ, баланс и модель, выбирает 9Router-ноду, проксирует запрос и возвращает ответ клиенту.

**Технические особенности:**
- Не сохранять request messages и response content.
- До вызова 9Router выполнить:
  - Auth gRPC: validate API key.
  - Billing gRPC: check positive balance and model availability.
  - Control Plane gRPC: select healthy least-loaded node.
- После ответа 9Router отправить usage event в Billing/Usage.
- Response должен быть максимально близок к OpenAI-compatible shape.

**Тесты:**
- Успешный запрос проходит весь pipeline и возвращает 200.
- При invalid API key возвращается 401.
- При balance <= 0 возвращается 402 или согласованный billing error.
- При неизвестной модели возвращается 404/400 OpenAI-compatible error.
- Проверить, что prompt/response bodies не пишутся в usage event.

### GATEWAY-003: Реализовать SSE streaming для `/v1/chat/completions`

**Постановка:** поддержать `stream=true`, прокинуть SSE chunks от 9Router клиенту и после завершения записать usage/cost event.

**Технические особенности:**
- Gateway не должен буферизовать весь ответ.
- Если upstream stream начался, прозрачный retry запрещен.
- Если ошибка случилась до первого SSE chunk, разрешен один retry на другой healthy node.
- Usage должен фиксироваться после финального usage payload от 9Router или после завершения stream по внутреннему контракту.

**Тесты:**
- Streaming response отдается chunk-by-chunk.
- Usage event записывается после завершения stream.
- Ошибка до первого chunk вызывает один retry на другой node.
- Ошибка после первого chunk не вызывает retry и корректно завершает stream/error.

### GATEWAY-004: Реализовать retry/failover policy для 9Router

**Постановка:** при сетевой ошибке или 5xx от выбранной ноды до начала streaming gateway должен один раз попробовать другую healthy-ноду.

**Технические особенности:**
- Retry только для safe scenarios: no response started.
- Не делать retry при 4xx от 9Router, invalid model, insufficient balance.
- Добавить idempotency/request id, чтобы Billing не получил двойной usage.
- Логировать первую ошибку с routerNodeId, но без тела prompt/response.

**Тесты:**
- 5xx на первой ноде приводит к вызову второй ноды.
- 4xx не ретраится.
- Если вторая нода успешна, клиент получает 200.
- Если обе ноды неуспешны, клиент получает OpenAI-compatible error.

### GATEWAY-005: Реализовать API key authentication middleware

**Постановка:** Gateway должен принимать `Authorization: Bearer <api_key>` и использовать Auth Service для проверки ключа.

**Технические особенности:**
- Raw API key никогда не логировать.
- Кэширование результата проверки можно добавить позже; в MVP gRPC validation на каждый запрос допустим.
- Auth response должен вернуть account/user id, key id, status.
- Disabled/revoked key отклоняется.

**Тесты:**
- Missing Authorization -> 401.
- Invalid scheme -> 401.
- Revoked key -> 401.
- Active key -> request context содержит accountId/apiKeyId.

### GATEWAY-006: Реализовать public error mapping

**Постановка:** все внутренние ошибки Gateway/Auth/Billing/Control/9Router должны возвращаться клиенту в OpenAI-compatible error body.

**Технические особенности:**
- Не раскрывать internal service names, stack traces, upstream credentials.
- Сохранять machine-readable `type`, `code`, `message`.
- Добавлять request/correlation id в headers.

**Тесты:**
- gRPC insufficient balance маппится в понятную billing error.
- Router unavailable маппится в 503.
- Internal exception маппится в 500 без sensitive details.

## 2. Auth Service

### AUTH-001: Реализовать модель пользователей и admin invites

**Постановка:** администратор может создать invite для пользователя; пользователь завершает регистрацию через email/password.

**Технические особенности:**
- Сущности: User, Invite, PasswordCredential.
- Invite имеет token hash, expiration, usedAt, createdByAdminId.
- Пароли хранить только как password hash с современным алгоритмом.
- На MVP email-доставку можно заменить отображением invite link/token в админке, если SMTP еще не подключен.

**Тесты:**
- Invite создается и может быть использован один раз.
- Просроченный invite не принимается.
- Повторное использование invite отклоняется.
- Password hash не равен raw password.

### AUTH-002: Реализовать login/session для Blazor UI

**Постановка:** пользователь и админ входят в кабинет по email/password.

**Технические особенности:**
- Поддержать роли: Admin, User.
- Session/JWT должен содержать userId, role, issuedAt, expiresAt.
- Failed login не должен раскрывать, существует ли email.
- Добавить logout.

**Тесты:**
- Валидные credentials создают сессию.
- Неверный пароль отклоняется.
- Disabled user не может войти.
- User без Admin role не получает доступ к admin endpoints.

### AUTH-003: Реализовать API key lifecycle

**Постановка:** пользователь может создать, переименовать и удалить API-ключ. Ключ показывается raw только один раз при создании.

**Технические особенности:**
- Хранить только hash ключа, prefix, lastUsedAt, revokedAt, displayName.
- Формат ключа должен позволять отличить продуктовые ключи, например `orr_...`.
- Delete лучше реализовать как revoke, не физическое удаление.
- Basic keys only: без scopes, IP allowlist, expiry и per-key limits.

**Тесты:**
- Создание возвращает raw key один раз.
- В базе хранится hash, raw key отсутствует.
- ValidateApiKey возвращает active key context.
- Revoked key не проходит validation.
- Rename не меняет secret/hash.

### AUTH-004: Реализовать gRPC Auth API

**Постановка:** предоставить внутренние gRPC methods для Gateway и UI.

**Технические особенности:**
- Минимальные методы:
  - `ValidateApiKey(rawKey)`.
  - `GetCurrentUser(userId)`.
  - `CreateInvite(...)`.
  - `CompleteInvite(...)`.
  - `CreateApiKey(userId, name)`.
  - `RevokeApiKey(userId, keyId)`.
- Raw API key принимает только validation/create path.

**Тесты:**
- Contract tests для `.proto`.
- gRPC integration tests с test server.
- Проверка error codes для invalid/revoked/disabled states.

## 3. Billing / Usage Service

### BILLING-001: Реализовать баланс и ledger

**Постановка:** сервис должен хранить баланс пользователя в $ и историю операций: virtual top-up, charge, correction.

**Технические особенности:**
- Ledger должен быть append-only.
- Current balance можно считать агрегатом, но он должен быть восстановим из ledger.
- Каждая операция имеет idempotency key.
- Negative balance допускается только как результат постфактум списания после дорогого запроса.

**Тесты:**
- Virtual top-up увеличивает баланс.
- Charge уменьшает баланс.
- Повтор операции с тем же idempotency key не меняет баланс дважды.
- Баланс восстанавливается из ledger.

### BILLING-002: Реализовать проверку баланса перед запросом

**Постановка:** Gateway должен через gRPC узнать, можно ли начинать запрос для account/user и модели.

**Технические особенности:**
- Правило MVP: запрос разрешен, если balance > 0 и модель включена.
- Pre-reservation не делаем.
- Возвращать public model id, pricing snapshot/version и разрешение.

**Тесты:**
- Balance > 0 -> allowed.
- Balance = 0 -> denied.
- Balance < 0 -> denied.
- Disabled model -> denied независимо от баланса.

### BILLING-003: Реализовать каталог моделей и pricing table

**Постановка:** админ управляет whitelist моделей, публичными model ids и ценами в рублях за input/output/cache tokens.

**Технические особенности:**
- Сущности: Model, ModelPrice, PriceVersion.
- Хранить upstream/9Router model mapping, но не отдавать его клиентам.
- Цена должна поддерживать input, output, cache token categories.
- Изменение цены не должно ломать историю: usage event хранит pricing snapshot/version.

**Тесты:**
- Enabled model появляется в `/v1/models`.
- Disabled model не появляется.
- Изменение цены влияет только на новые usage events.
- Расчет стоимости использует правильную price version.

### BILLING-004: Реализовать прием usage events

**Постановка:** Gateway после ответа/stream завершения отправляет usage event с token counts от 9Router.

**Технические особенности:**
- Usage event содержит requestId, userId/accountId, apiKeyId, modelId, routerNodeId, inputTokens, outputTokens, cacheTokens, status, latency, startedAt, completedAt.
- Не содержит prompt/response bodies.
- Event должен быть идемпотентен по requestId.
- При отсутствующем или невалидном usage создать billing anomaly и не списывать автоматически без правила.

**Тесты:**
- Валидный usage event создает charge ledger entry.
- Повтор usage event с тем же requestId не списывает повторно.
- Missing usage создает anomaly.
- Negative token count отклоняется.

### BILLING-005: Реализовать расчет стоимости

**Постановка:** вычислять стоимость запроса в $ по input/output/cache tokens и таблице цен модели.

**Технические особенности:**
- Формула: `(inputTokens * inputPricePer1M + outputTokens * outputPricePer1M + cacheTokens * cachePricePer1M) / 1_000_000`.
- Округление определить явно: хранить точность decimal, отображение в UI округлять отдельно.
- Сохранять breakdown в usage/charge record.

**Тесты:**
- Data-driven tests для разных комбинаций токенов.
- Проверка cache token pricing.
- Проверка нулевых token categories.
- Проверка отсутствующей цены вызывает anomaly/error.

### BILLING-006: Реализовать usage aggregates

**Постановка:** подготовить агрегаты для кабинета и админки: usage by day, model, user, status, cost.

**Технические особенности:**
- Агрегация через PostgreSQL outbox/background workers.
- Агрегаты не должны быть source of truth для денег; source of truth - ledger и usage events.
- Поддержать пересчет агрегатов за период.

**Тесты:**
- Worker строит daily aggregate после usage event.
- Повторная обработка outbox event не дублирует агрегат.
- Rebuild за период дает тот же результат.

### BILLING-007: Реализовать gRPC Billing API

**Постановка:** предоставить Gateway и UI внутренние методы для баланса, моделей, usage и admin top-ups.

**Технические особенности:**
- Минимальные методы:
  - `CheckBalanceAndModel(...)`.
  - `RecordUsage(...)`.
  - `ListPublicModels(...)`.
  - `GetBalance(userId)`.
  - `CreateVirtualTopUp(adminId, userId, amount, reason)`.
  - `UpsertModelPrice(...)`.
  - `ListUsage(...)`.
  - `ListBillingAnomalies(...)`.

**Тесты:**
- gRPC integration tests для success/failure paths.
- Проверка role/authorization для admin-only methods.
- Contract tests для обязательных полей.

## 4. Router Control Plane Service

### CONTROL-001: Реализовать registry 9Router-нodi

**Постановка:** админ может добавить, изменить, включить и отключить внешнюю 9Router-ноду.

**Технические особенности:**
- Сущность RouterNode: id, name, endpointUrl, tokenSecretRef/hash, capacityWeight, enabled, metadata, createdAt, updatedAt.
- Endpoint должен быть HTTPS.
- Token хранить защищенно; raw token показывать только при создании или использовать secret reference.
- Disabled node не участвует в выборе.

**Тесты:**
- Нода создается с валидным HTTPS endpoint.
- HTTP endpoint отклоняется.
- Disabled node не возвращается в candidate list.
- Token не возвращается в read models.

### CONTROL-002: Реализовать health checks 9Router-нodi

**Постановка:** сервис регулярно проверяет health endpoint каждой enabled ноды и сохраняет статус.

**Технические особенности:**
- Проверять через HTTPS + static node token.
- Сохранять lastStatus, lastCheckedAt, latency, errorCode, version/capabilities если доступны.
- Healthy status должен учитывать несколько последних проверок или cooldown, чтобы избежать flapping.

**Тесты:**
- Healthy response переводит ноду в Healthy.
- Timeout переводит ноду в Unhealthy/Degraded.
- Disabled node не проверяется.
- Flapping не приводит к хаотичному переключению статуса при выбранной политике.

### CONTROL-003: Реализовать least-load selection

**Постановка:** Gateway запрашивает лучшую ноду для запроса, Control Plane возвращает healthy least-loaded endpoint.

**Технические особенности:**
- Учитывать only enabled + healthy nodes.
- Load можно считать из in-flight counters, recent latency/error rate или admin capacity weight. Для MVP допустимо: healthy nodes + capacityWeight + recent inFlight/load metric.
- Если нет доступных нод, вернуть typed gRPC error `ROUTER_UNAVAILABLE`.

**Тесты:**
- При одной healthy node возвращается она.
- Unhealthy node не выбирается.
- Менее загруженная нода выбирается перед более загруженной.
- При отсутствии healthy nodes возвращается ошибка.

### CONTROL-004: Реализовать учет in-flight/load hints

**Постановка:** Gateway должен сообщать Control Plane о начале/завершении запроса или Control Plane должен принимать load hints для корректного least-load выбора.

**Технические особенности:**
- Минимальный вариант: Gateway вызывает `LeaseRouterNode` и `ReleaseRouterNode`.
- Lease имеет ttl, чтобы зависшие запросы не держали load навсегда.
- Release идемпотентен.

**Тесты:**
- Lease увеличивает in-flight counter.
- Release уменьшает counter.
- Повторный Release не уводит counter ниже нуля.
- Expired lease автоматически освобождается worker-ом.

### CONTROL-005: Реализовать gRPC Control Plane API

**Постановка:** предоставить Gateway и Admin UI методы выбора и управления router nodes.

**Технические особенности:**
- Минимальные методы:
  - `SelectRouterNode(modelId, requestId)`.
  - `ReleaseRouterNode(leaseId, status)`.
  - `CreateRouterNode(...)`.
  - `UpdateRouterNode(...)`.
  - `DisableRouterNode(...)`.
  - `ListRouterNodes(...)`.
  - `GetRouterNodeStatus(...)`.

**Тесты:**
- gRPC integration tests для select/release.
- Admin-only методы недоступны обычному user.
- Error mapping для no healthy nodes.

## 5. Blazor User Cabinet / Admin Dashboard

### UI-001: Реализовать login и invite completion screens

**Постановка:** пользователь должен завершить invite и войти в кабинет.

**Технические особенности:**
- UI вызывает Auth gRPC через backend/server-side Blazor layer.
- Ошибки показывать без раскрытия sensitive details.
- После login перенаправлять по роли: User -> cabinet, Admin -> admin dashboard.

**Тесты:**
- Component tests для validation states.
- E2E smoke: complete invite -> login -> dashboard.
- User не видит admin navigation.

### UI-002: Реализовать пользовательский экран API keys

**Постановка:** пользователь создает, переименовывает и отзывает API-ключи.

**Технические особенности:**
- Raw key показывать только один раз после создания.
- Список показывает name, prefix, createdAt, lastUsedAt, status.
- Для revoke требовать подтверждение.

**Тесты:**
- Создание ключа показывает raw value один раз.
- После refresh raw value недоступен.
- Revoke убирает ключ из active list или помечает revoked.

### UI-003: Реализовать пользовательский экран balance/usage

**Постановка:** пользователь видит текущий баланс, историю списаний и usage по моделям.

**Технические особенности:**
- Данные получать из Billing/Usage gRPC.
- Показывать usage без prompt/response bodies.
- Поддержать фильтр по датам и моделям.

**Тесты:**
- Balance отображается в $.
- Usage list корректно показывает tokens/cost/status.
- Фильтр по периоду меняет выдачу.

### UI-004: Реализовать admin user management

**Постановка:** админ управляет пользователями, invite и виртуальными пополнениями.

**Технические особенности:**
- Создать invite.
- Посмотреть пользователя, баланс, usage summary.
- Сделать virtual top-up с reason/comment.
- Disabled user должен терять доступ к login/API key validation.

**Тесты:**
- Admin создает invite.
- Admin делает top-up, баланс пользователя меняется.
- User role не может открыть admin user management.

### UI-005: Реализовать admin model/pricing management

**Постановка:** админ включает модели, задает public id, 9Router mapping и $ prices.

**Технические особенности:**
- Форма должна валидировать цены >= 0.
- Изменение цены создает новую version/snapshot.
- Disabled model скрывается из public `/v1/models`.

**Тесты:**
- Создание модели с ценами проходит.
- Негативная цена отклоняется.
- Disabled model не виден пользователю.

### UI-006: Реализовать admin router node dashboard

**Постановка:** админ видит 9Router-ноды, их статус, load, latency, ошибки и может включать/отключать ноды.

**Технические особенности:**
- Данные из Router Control Plane gRPC.
- Отображать Healthy/Degraded/Unhealthy/Disabled.
- Дать действие disable/enable.
- Не показывать node token.

**Тесты:**
- Healthy node отображается корректно.
- Disabled node не помечается как доступная для routing.
- Token secret не отображается в UI.

### UI-007: Реализовать admin billing anomalies screen

**Постановка:** админ видит usage/billing anomalies: missing usage, invalid token counts, unknown model price, failed charge.

**Технические особенности:**
- Источник данных - Billing/Usage.
- Для MVP достаточно read-only списка и статуса.
- Добавить фильтры: date, anomaly type, status.

**Тесты:**
- Anomaly появляется в списке.
- Фильтры работают.
- User role не имеет доступа.

## 6. 9Router Fork / Internal Adapter Contract

### ROUTER-001: Добавить internal health endpoint

**Постановка:** 9Router-нода должна предоставлять health endpoint для Control Plane.

**Технические особенности:**
- Endpoint требует static node token.
- Response содержит status, version, capabilities, optional current load.
- Не раскрывать upstream provider secrets.

**Тесты:**
- Без token -> 401.
- С token -> 200 и корректный health payload.
- При недоступном upstream статус может быть Degraded.

### ROUTER-002: Стандартизировать usage payload

**Постановка:** 9Router должен возвращать или передавать Gateway usage с input/output/cache tokens.

**Технические особенности:**
- Единая схема usage для non-streaming и streaming.
- Обязательные поля: model, inputTokens, outputTokens, cacheTokens, provider/upstream status.
- Если provider не вернул usage, 9Router должен явно пометить usage как missing/estimated, а не молча отдавать нули.

**Тесты:**
- Non-streaming response содержит usage.
- Streaming завершение содержит финальный usage payload/metadata.
- Missing provider usage помечается явно.

### ROUTER-003: Стандартизировать error shape для Gateway

**Постановка:** ошибки 9Router должны иметь стабильный формат, чтобы Gateway мог корректно маппить их клиенту.

**Технические особенности:**
- Error fields: code, message, retryable, upstreamStatus, provider, requestId.
- Не отдавать upstream credentials/secrets.
- 4xx и 5xx должны различаться.

**Тесты:**
- Upstream 401/403 не раскрывает секреты.
- Upstream timeout помечен retryable.
- Invalid model помечен non-retryable.

### ROUTER-004: Поддержать internal node token

**Постановка:** 9Router должен принимать запросы от Gateway только с валидным node token.

**Технические особенности:**
- Token передается в internal header, например `X-ORR-Node-Token`.
- Token хранится в server env/secret.
- Unauthorized requests отклоняются до обращения к upstream provider.

**Тесты:**
- Нет token -> 401.
- Неверный token -> 401.
- Верный token -> запрос проходит.

### ROUTER-005: Зафиксировать model mapping contract

**Постановка:** 9Router должен принимать внутренний model id/mapping от Gateway и маршрутизировать его к настроенной upstream модели.

**Технические особенности:**
- Клиентский public model id не обязан совпадать с upstream provider model id.
- Mapping должен быть стабильным и понятным для Billing.
- Ошибка неизвестной модели должна быть typed и non-retryable.

**Тесты:**
- Известная модель маршрутизируется корректно.
- Неизвестная модель возвращает typed error.
- Usage возвращается с model id, который Billing может связать с price snapshot.

## 7. Kubernetes / Infrastructure Service Layer

### INFRA-001: Подготовить Kubernetes manifests/Helm chart для .NET сервисов

**Постановка:** описать деплой API Gateway, Auth, Billing/Usage, Control Plane и Blazor UI.

**Технические особенности:**
- Для каждого workload: Deployment, Service, ServiceAccount, ConfigMap/Secret references.
- Обязательны resource requests/limits.
- Обязательны readiness/liveness probes.
- Не использовать `latest` image tag.

**Тесты:**
- `helm template` или manifest render проходит без ошибок.
- Kubernetes schema validation проходит.
- Все containers имеют probes и resources.

### INFRA-002: Настроить NetworkPolicies

**Постановка:** ограничить сетевые связи внутри namespace.

**Технические особенности:**
- Default deny ingress/egress.
- Разрешить Ingress -> Gateway.
- Разрешить Gateway -> Auth/Billing/Control gRPC.
- Разрешить Gateway -> external 9Router endpoints.
- Разрешить сервисам доступ только к своим PostgreSQL endpoints.

**Тесты:**
- Policy manifests проходят validation.
- Проверка сценариев allowed/denied traffic в staging/minikube/kind.

### INFRA-003: Настроить Secrets и configuration

**Постановка:** все чувствительные параметры хранить в Kubernetes Secrets.

**Технические особенности:**
- PostgreSQL connection strings, JWT/session secrets, node tokens, admin bootstrap credentials.
- ConfigMaps только для нечувствительных настроек.
- Поддержать environment-specific values.

**Тесты:**
- В manifests нет hardcoded secrets.
- Сервис не стартует без обязательных секретов.
- Strongly typed config validation ловит пустые/некорректные значения.

### INFRA-004: Подготовить PostgreSQL provisioning/migrations

**Постановка:** обеспечить отдельные базы/пользователей/миграции для Auth, Billing и Control Plane.

**Технические особенности:**
- У каждого сервиса свой DB user с правами только на свою базу/schema.
- Миграции запускаются явно через job/init pipeline, не неявно в runtime без контроля.
- Backup/restore требования зафиксировать для pilot environment.

**Тесты:**
- Миграции применяются на пустую базу.
- Сервисный DB user не может читать чужую базу/schema.
- Roll-forward миграции проходят в тестовом окружении.

## 8. End-to-End MVP сценарии

### E2E-001: Полный happy path пилотного клиента

**Постановка:** проверить цепочку admin invite -> user login -> API key -> virtual top-up -> chat completion -> usage -> charge -> dashboard.

**Технические особенности:**
- Использовать fake/mock 9Router или staging 9Router node.
- Проверять отсутствие prompt/response bodies в DB/logs.
- Проверять gRPC calls между сервисами.

**Тесты:**
- E2E test проходит весь сценарий.
- Баланс уменьшается на рассчитанную стоимость.
- Usage отображается в user cabinet и admin dashboard.

### E2E-002: Баланс блокирует запросы

**Постановка:** пользователь с нулевым/отрицательным балансом не может выполнить chat completion.

**Технические особенности:**
- Проверка должна происходить до обращения к 9Router.
- Error response должен быть OpenAI-compatible.

**Тесты:**
- Balance = 0 -> 9Router не вызывается.
- Balance < 0 -> 9Router не вызывается.
- Response не раскрывает внутренние Billing details.

### E2E-003: Failover между 9Router-ноди

**Постановка:** если первая healthy нода падает до начала ответа, Gateway делает один retry на другую ноду.

**Технические особенности:**
- Для streaming retry разрешен только до первого SSE chunk.
- Usage должен записаться один раз.

**Тесты:**
- Первая нода возвращает 5xx, вторая 200 -> клиент получает 200.
- Usage event один.
- Control Plane получает release/update статуса lease.

### E2E-004: No prompt storage verification

**Постановка:** убедиться, что пользовательский prompt и ответ модели не попадают в persistent storage и обычные логи.

**Технические особенности:**
- Использовать уникальную marker string в prompt и fake response.
- Проверить базы Auth/Billing/Control и captured logs.

**Тесты:**
- Marker string отсутствует в PostgreSQL tables.
- Marker string отсутствует в application logs.
- Usage metadata при этом сохранена.

## Рекомендуемый порядок реализации

1. PLATFORM-001, PLATFORM-002, PLATFORM-003.
2. AUTH-001, AUTH-002, AUTH-003, AUTH-004.
3. BILLING-001, BILLING-002, BILLING-003, BILLING-004, BILLING-005.
4. CONTROL-001, CONTROL-002, CONTROL-003, CONTROL-004, CONTROL-005.
5. ROUTER-001, ROUTER-002, ROUTER-003, ROUTER-004, ROUTER-005.
6. GATEWAY-001, GATEWAY-002, GATEWAY-003, GATEWAY-004, GATEWAY-005, GATEWAY-006.
7. UI-001, UI-002, UI-003, UI-004, UI-005, UI-006, UI-007.
8. INFRA-001, INFRA-002, INFRA-003, INFRA-004.
9. E2E-001, E2E-002, E2E-003, E2E-004.
