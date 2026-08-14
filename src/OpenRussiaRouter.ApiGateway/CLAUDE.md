# ApiGateway — CLAUDE.md

Публичный HTTP-вход платформы, OpenAI-совместимый API. **Только HTTP-сервер** — gRPC не хостит,
к Auth/Billing/ControlPlane ходит как gRPC-клиент, к нодам 9Router — напрямую HTTP.
**У сервиса есть своя БД** — схема `gateway` (одна таблица `usage_outbox`), миграции
`db/migrations/gateway/` применяются на старте.

Порт: в appsettings Kestrel не сконфигурен. Локально — `Properties/launchSettings.json`
(`http://localhost:5268`), в контейнере — `ASPNETCORE_URLS=http://+:8080` из [Dockerfile](Dockerfile)
(в compose наружу маппится `8080:8080`). «5000» в корневом индексе — логический порт сервиса.

## Точка входа и bootstrap

[Program.cs](Program.cs):
1. `AddSeqLogging("api-gateway")` + `AddOpenTelemetryMetrics(...)`;
2. `Startup.ConfigureServices` ([Startup.cs](Infrastructure/Startup/Startup.cs));
3. **`ApplySqlMigrationsAsync("GatewayDb", "gateway", …)`** — до `ConfigurePipeline`, гейтится
   `SqlMigrations:Enabled` (дефолт `true`). Без валидной `ConnectionStrings:GatewayDb` старт падает;
4. `Startup.ConfigurePipeline` + `MapOpenTelemetryEndpoints` (Prometheus `/metrics`).

`public partial class Program;` в конце файла нужен для `WebApplicationFactory<Program>` в тестах — не удалять.

`ConfigureServices` — пять extension-методов из [ApiGatewayExtensions.cs](Application/Extensions/ApiGatewayExtensions.cs):

| Метод | Что регистрирует |
|---|---|
| `AddGatewayGrpcClients` | gRPC-клиенты Auth/Billing/ControlPlane + scoped `GrpcApiKeyProvider` / `GrpcBillingProvider` / `GrpcControlPlaneProvider` |
| `AddGatewayRouter` | YARP (`AddReverseProxy`) + `YarpRouterProxyProvider` |
| `AddGatewayHandlers` | `GatewayRetryOptions`, `IGatewayPipeline`, четыре хендлера |
| `AddGatewayUsageOutbox` | `GatewayDbConnectionFactory`, `PostgresUsageOutboxRepository`, `UsageOutboxOptions`, hosted `UsageOutboxDispatcher` |
| `AddGatewayRateLimiting` | политика `gateway-chat` ([GatewayRateLimiting.cs](Application/Extensions/GatewayRateLimiting.cs)) |

Порядок middleware в `ConfigurePipeline`: `UseHttpLogging` → (вне Development) `UseLoggedExceptionHandler("/error")` + `UseHsts`
→ инлайн-middleware, проставляющий в ответ `X-Correlation-Id` **и** `X-Request-Id` → `VisitorMetricsMiddleware`
→ `UseRateLimiter` → `MapGatewayEndpoints`.

## Эндпоинты (`MapGatewayEndpoints`)

| Эндпоинт | Handler | Rate limit | Usage/биллинг |
|---|---|---|---|
| `POST /v1/chat/completions` | `ChatCompletionHandler.HandleAsync` (при `"stream":true` в теле сам уходит в streaming-путь) | `gateway-chat` | outbox, токены из тела/финального SSE-чанка |
| `POST /v1/chat/completions/stream` | `ChatCompletionHandler.HandleStreamAsync` | `gateway-chat` | outbox, токены из финального SSE-чанка |
| `POST /v1/images/generations` | [ImageGenerationHandler](Application/Endpoints/ImageGenerationHandler.cs) | `gateway-chat` | **usage не пишется — картинки не биллятся** |
| `POST /v1/audio/speech` | [TextToSpeechHandler](Application/Endpoints/TextToSpeechHandler.cs) | `gateway-chat` | outbox, `output_tokens` = кол-во Unicode code points (`EnumerateRunes().Count()`) |
| `GET /v1/models` | [ModelsHandler](Application/Endpoints/ModelsHandler.cs) | — | — |
| `GET /health`, `GET /ready` | инлайн-лямбды | — | — |

`/v1/models` **сознательно публичный** (без Bearer) — OpenRouter-совместимые клиенты (Cline, opencode-плагин)
пробуют его до передачи ключа.

## Пайплайн авторизации — `GatewayPipeline` ([файл](Application/Services/GatewayPipeline.cs))

`AuthorizeAndAcquireLeaseAsync` — единственная точка авторизации для всех четырёх POST-ов.
Любой провал → `GatewayErrors.WriteAsync` в ответ + `null` наружу:

1. `model` не пуст;
2. `BearerTokenReader.TryRead` достаёт токен из `Authorization: Bearer`;
3. `authProvider.AuthenticateAsync` → AuthService (`UserId`, `ApiKeyId`, `ApiKeyPrefix`, `IsAdmin` = `Role == "Admin"`);
4. `billingProvider.CheckBalanceAndModelAsync(userId, **apiKeyId**, model, adminBypassDisabled)` → Billing.
   `apiKeyId` обязателен — по нему Billing считает spend-лимиты ключа. Маппинг кодов в OpenAI-типы:
   `InsufficientBalance`/`ApiKeyLimitExceeded` → `billing_error` (402), `ModelUnavailable` → `invalid_request_error` (400),
   `InternalError` → `internal_error`, остальное → `invalid_request_error`;
5. `controlPlaneProvider.SelectRouterNodeAsync(...)` → ControlPlane; `null` → 503 `RouterUnavailable`.

**В ControlPlane передаётся `balanceResult.RouterModelId`** (upstream-ID ноды, напр. `glm-4.7`),
а не публичный billing-ID (`ORR/GLM-4.7`) — иначе capability-store не сматчит модель.

Возвращает [GatewayAuthorizedContext](Application/Services/GatewayAuthorizedContext.cs): ключ, user/apiKey-идентификаторы,
`RouterModelId`, лиза, и **reasoning-метаданные модели** (`SupportsReasoning`, `AllowedReasoningEfforts`,
`DefaultReasoningEffort`) — они приходят из Billing вместе с проверкой баланса.

Спецзаголовки запроса:

| Заголовок | Эффект |
|---|---|
| `X-ORR-Admin-Test: 1` | от админ-ключа → `adminBypassDisabled=true`, Billing разрешает disabled-модели |
| `X-ORR-Preferred-Node-Id` + `X-ORR-Preferred-Node-Token` | привязка к конкретной ноде (оба обязательны, иначе игнорируются); константы — [GatewayPreferredNodeHeaders](Application/Services/GatewayPreferredNodeHeaders.cs). Используется `/admin/gatewaytest` в WebApp |

## Retry / failover — общая схема всех хендлеров

Все четыре POST-а крутят один и тот же цикл: `AuthorizeAndAcquireLeaseAsync(excludedNodes)` → патч тела →
отправка на ноду → `ReleaseRouterNodeAsync(leaseId, "completed"|"failed")`. При ошибке нода добавляется
в `excludedNodes`, счётчик `attempts` инкрементится, цикл повторяется.

- **`MaxRouterAttempts`**: в `ChatCompletionHandler` — из конфига `GatewayRetry:MaxRouterAttempts`
  ([GatewayRetryOptions](Application/Extensions/GatewayOptions.cs), дефолт 5, `[Range(1,20)]`, секции в appsettings нет).
  В `ImageGenerationHandler`/`TextToSpeechHandler` — **захардкоженная `const int MaxRouterAttempts = 5`**.
- Ноды кончились (pipeline вернул `null` на повторной итерации) → наружу отдаётся `lastResponse` от последней ноды.
- **`HttpRequestException`/`IOException` трактуются как 5xx** — соединение порвалось до/во время получения ответа
  (краш ноды, протухший pooled HTTP/2-коннект): release `failed`, exclude, retry. Если попытки исчерпаны и
  `lastResponse` нет — 503 `RouterUnavailable`.
- **`ReleaseRouterNodeAsync` всегда вызывается с `CancellationToken.None`** — лиза должна освободиться даже
  если клиент отключился. Не заменять на `context.RequestAborted`.
- `OperationCanceledException` = клиент отключился, это норма (release + rethrow, наружу ничего не пишем).

**Streaming (`ExecuteStreamingAsync`)**: `BeginStreamChatCompletionAsync` → `RouterStreamSession`
(`HttpCompletionOption.ResponseHeadersRead`). Ретрай возможен **только пока `!session.IsSuccess`** —
предыдущая failed-сессия диспозится, тело её ошибки может уехать наружу как pass-through.
После первого успешного `session` выставляются SSE-заголовки (`text/event-stream`, `Cache-Control: no-cache`,
`X-Accel-Buffering: no`) и `CopyToAsync` начинает стримить — **после коммита ответа ни сменить ноду,
ни отдать JSON-ошибку уже нельзя**.

## Патчи тела запроса ([OpenAi/](OpenAi/))

Порядок для chat completions: `PatchModel` → `ReasoningRequestPatcher` → `GeminiSchemaPatcher`
→ (только stream) `InjectStreamOptions`.

- [ChatCompletionRequestReader](OpenAi/ChatCompletionRequestReader.cs) — читает тело один раз, отдаёт
  `ChatCompletionRequestBody(Json, Metadata{Model, Stream, Reasoning})`. Тело сырое (строка) —
  все патчеры работают со строкой и **на `JsonException` возвращают вход как есть**, не падают.
- [SseRequestPatcher](OpenAi/SseRequestPatcher.cs) — `PatchModel` (подмена `model` на `RouterModelId`)
  и `InjectStreamOptions` (`stream:true` + `stream_options.include_usage:true`, чтобы нода вернула usage
  в финальном чанке; существующий `stream_options` не перезаписывается).
- [ReasoningRequestPatcher](OpenAi/ReasoningRequestPatcher.cs) — нормализация reasoning. Итог всегда
  **плоский `reasoning_effort`**: `InjectEffort` удаляет объект `reasoning` и пишет строку.
  - клиент ничего не прислал → подставляется `DefaultReasoningEffort` модели (если задан);
  - `reasoning.max_tokens`/`reasoning.enabled` → ошибка `UnsupportedReasoning`;
  - `reasoning_effort` ≠ `reasoning.effort` → `ConflictingParameters`;
  - модель не поддерживает reasoning → `UnsupportedReasoning`;
  - effort не из `AllowedReasoningEfforts`: есть дефолт → **молча подменяется на дефолт**, нет → `UnsupportedReasoningEffort`;
  - дефолт, которого нет в allow-list, игнорируется (защита от кривого админского конфига).
  Ошибка патчера = 400 + release лизы `failed`.
- [GeminiSchemaPatcher](OpenAi/GeminiSchemaPatcher.cs) — если `RouterModelId` **или** оригинальный `model`
  содержит `gemini`, инлайнит `$ref`/`$defs` в JSON-схемах `tools`/`functions`/`response_format`
  и в свойствах `parameters`/`schema`/`input_schema`/`parameters_json_schema` (Gemini не понимает `$ref`),
  рекурсивно, с защитой от циклов (набор `resolving` + `depth > 32`).

## `/v1/models` — контракт OpenRouter

`ModelsHandler` отдаёт каталог из Billing (`ListPublicModelsAsync`) в формате, выровненном с OpenRouter:

- **top-level объект `reasoning`** (`mandatory:false`, опционально `supported_efforts`, `default_effort`) —
  **само его наличие** является для клиентов сигналом «модель умеет reasoning». У non-reasoning моделей
  поле должно **полностью отсутствовать**, не быть `null` → отсюда `DefaultIgnoreCondition = WhenWritingNull`;
- `supported_parameters` — базовый набор + `include_reasoning`/`reasoning`/`reasoning_effort` для reasoning-моделей,
  отсортирован `StringComparer.Ordinal`;
- дополнительный блок `capabilities.reasoning` — только для ORR-плагина opencode и своей админки.

## Прокси к ноде ([YarpRouterProxyProvider.cs](Router/YarpRouterProxyProvider.cs))

- **Два static HTTP-клиента**: `ForwardingHttpClient` (`HttpMessageInvoker` для YARP `IHttpForwarder`,
  non-stream) и `StreamingHttpClient` (обычный `HttpClient`, `Timeout` 30 мин). Оба: `UseProxy=false`,
  `AutomaticDecompression=None`, HTTP/2 multiplexing, `ConnectTimeout=15s`,
  `PooledConnectionLifetime=2 мин`. `ForwarderRequestConfig.ActivityTimeout = 5 мин`.
- Non-stream пути собираются через фиктивный `DefaultHttpContext` с `MemoryStream` в качестве ответа
  и `forwarder.SendAsync(destinationPrefix, …)`; `ForwarderError != None` → `InvalidOperationException`.
  **Она не подпадает под retry** (хендлеры ретраят только `HttpRequestException`/`IOException`) —
  лиза освобождается `failed`, клиент получает 500 `internal_error`.
- На ноду уходит `Authorization: Bearer <userApiKey>` (пользовательский `sk-or-*`, **не** control-token
  из лизы!) + `X-Correlation-Id`. Путь на ноде совпадает с публичным (`/v1/chat/completions`,
  `/v1/images/generations`, `/v1/audio/speech`).
- Provider-метаданные снимаются с заголовков ответа ноды `x-9r-provider` / `x-9r-account` → в usage-событие.
- **TTS**: если `Content-Type` начинается с `audio/`, тело возвращается в `RouterProxyResponse.BodyBytes`
  и пишется через `Response.BodyWriter` — не гонять бинарь через строку.

[RouterStreamSession](Router/RouterStreamSession.cs) — `CopyToAsync` построчно читает SSE, пересобирает
кадры как `data: <payload>\n\n` с флашем на каждый, обрабатывает `data: [DONE]`, и по пути забирает
usage из последнего чанка с ненулевыми токенами. **Всё, что не начинается с `data:` (опционально с пробелом), отбрасывается**
(`event:`, комментарии, кастомные поля не доедут до клиента).

[RouterUsageExtractor](Router/RouterUsageExtractor.cs) — тонкая обёртка над `TokenUsageExtractor`
(BuildingBlocks): сначала JSON, если токенов 0 — SSE.

## Usage-outbox ([Billing/](Billing/))

Usage **не отправляется в Billing из хот-пасса** — хендлер кладёт `GatewayUsageEvent` в
`gateway.usage_outbox`, а доставляет фоновой `UsageOutboxDispatcher`.

- `RecordUsageAsync` в хендлерах — `try/catch`, ошибка enqueue только логируется (`LogError`),
  ответ клиенту не ломается. Статус события: `completed` если токены > 0, иначе `usage_missing`.
- [PostgresUsageOutboxRepository](Billing/PostgresUsageOutboxRepository.cs):
  `EnqueueAsync` — `on conflict (request_id) do nothing` (идемпотентность по `request_id` = correlation id);
  `ClaimPendingAsync` — `update … where ctid in (select … for update skip locked)` под транзакцией,
  ставит `leased_until_utc`; `MarkSentAsync` — **`delete`** (отправленные строки не хранятся);
  `MarkFailedAsync` — инкремент `attempts` + `next_attempt_at_utc`, снятие лизы.
- [UsageOutboxDispatcher](Billing/UsageOutboxDispatcher.cs) — `BackgroundService`, каждые `PollInterval`
  забирает батч и шлёт `RecordUsageAsync`. `accepted == false` от Billing → строка тоже удаляется
  (не бесконечно ретраится). Экспоненциальный бэкофф `BaseBackoff * 2^(attempts-1)`, **клампится 1 часом**
  (иначе `Int64`-оверфлоу превращал бы бэкофф в hot loop). Превышение `MaxAttempts` **не удаляет строку** —
  только повышает лог до `LogError`, ретраи продолжаются.
- `InMemoryUsageOutboxRepository` — **только для тестов**, в DI не регистрируется.

Клиент Billing (`BillingGrpc.BillingServiceClient`) обёрнут resilience-пайплайном `billing-usage`:
retry 6 попыток, экспонента от 4с, на `RpcException`/`HttpRequestException`, плюс circuit breaker
(окно 30с, порог 0.5, минимум 10 запросов, разрыв 15с). То есть у usage-доставки два уровня ретраев —
Polly внутри вызова и outbox снаружи.

## Rate limiting ([GatewayRateLimiting.cs](Application/Extensions/GatewayRateLimiting.cs))

Политика `gateway-chat` (`GatewayRateLimiting.PolicyName`), fixed window, партиция —
`key:<SHA256(apiKey)[..8]>` либо фолбэк `ip:<remoteIp>` для запросов без Bearer. `QueueLimit = 0`.
`RateLimiting:Enabled=false` → `GetNoLimiter`, политика становится no-op (снимать `RequireRateLimiting`
с эндпоинтов для отладки не нужно).

`OnRejected`: сначала `LimitMetrics.RecordRejection(RateLimit, ApiKey|Ip)` — **до** проверки `HasStarted`,
затем `Retry-After` и OpenAI-ошибка `rate_limit_error` / `ErrorCodes.RateLimited` (429).

## Ошибки и correlation

- [GatewayErrors.WriteAsync](Http/GatewayErrors.cs) — единственный способ отдать ошибку из хендлеров:
  пишет `{error:{type, code, message, request_id}}`, маппит `type` → HTTP-статус, дублирует id
  в `X-Correlation-Id` и `X-Request-Id`, **no-op при `Response.HasStarted`**.
  `IResult`-хелперы (`Authentication`, `Validation`, `Billing`, …) в том же файле сейчас не используются продом.
- [CorrelationIdReader.Get](Http/CorrelationIdReader.cs) — берёт `X-Correlation-Id` из запроса
  либо генерит новый. Этот же id = `requestId` = PK в outbox.
- [VisitorMetricsMiddleware](Infrastructure/Startup/VisitorMetricsMiddleware.cs) — `orr_http_requests_total`,
  `orr_http_request_duration_seconds`, `orr_http_response_size_bytes`. Путь нормализуется
  (`NormalizePath`), чтобы не плодить серии. Лейблы: `method`, `path`, `status`.

## Конфигурация

| Ключ | Дефолт / где | Заметки |
|---|---|---|
| `ConnectionStrings:GatewayDb` | appsettings (localhost) | обязателен при `SqlMigrations:Enabled=true`; в compose переопределяется на `postgres` |
| `SqlMigrations:Enabled` | `true` | `false` — единственный способ поднять сервис без Postgres (так делают интеграционные тесты) |
| `GrpcServices:Auth` / `Billing` / `ControlPlane` | `http://localhost:5001/5002/5003` | читаются напрямую из `IConfiguration` |
| `GrpcServices:CallDeadlineSeconds` | 30 | `GrpcDeadlineInterceptor` на всех трёх клиентах |
| `ServiceAuth:BillingCaller` / `BillingToken` | `apigateway` / значение в appsettings | **пустой `BillingToken` → `InvalidOperationException` на старте** (TS-003), это фича, не ослаблять |
| `RateLimiting:Enabled` / `PermitLimit` / `WindowSeconds` | `true` / 60 / 60 | |
| `UsageOutbox:BatchSize` / `PollInterval` / `MaxAttempts` / `BaseBackoff` / `LeaseDuration` | 50 / 2с / 10 / 5с / 1 мин | |
| `GatewayRetry:MaxRouterAttempts` | 5 | секции в appsettings нет, только дефолт из кода |

Особенность `.csproj`: SDK — `Microsoft.NET.Sdk`, не `Sdk.Web`, поэтому `appsettings*.json` подключены
**вручную** через `<Content Include=…>`. Добавляешь новый appsettings-файл — проверь, что он попадает в publish.

## Тесты ([tests/OpenRussiaRouter.ApiGateway.Tests](../../tests/OpenRussiaRouter.ApiGateway.Tests))

- Интеграционные (`Integration/`) — `WebApplicationFactory<Program>` + `UseSetting("SqlMigrations:Enabled","false")`
  и `ConfigureTestServices`, подменяющий `IApiKeyProvider` / `IBillingProvider` / `IControlPlaneProvider` /
  `IRouterProxyProvider` / `IUsageOutboxRepository` на стабы, а `UsageOutboxOptions.PollInterval` — на 1 час
  (чтобы диспетчер не мешал). Покрыто: коды ошибок пайплайна, preferred-node заголовки, admin-байпас,
  retry/failover, streaming-ретрай, rate limiting, TTS, контракт `/v1/models`, отсутствие промптов в usage-событии (E2E-004).
- `Billing/PostgresUsageOutboxRepositoryTests` — Testcontainers, скипается без Docker.
- `InternalsVisibleTo` на тестовую сборку прописан в `.csproj`.

## Подводные камни

- **Миграция `002_usage_outbox_status_check.sql` разрешает только `pending|processing|completed|failed`,
  а хендлеры пишут `usage_missing`** — такой insert валится check-constraint'ом, ловится `try/catch`
  в `RecordUsageAsync` и превращается в потерянное usage-событие с `LogError`. Тесты этого не ловят:
  интеграционные используют in-memory outbox, а Postgres-тест пишет только `completed`.
- **`GatewayGrpcOptions` и `GatewayHttpClientOptions`** ([GatewayOptions.cs](Application/Extensions/GatewayOptions.cs))
  зарегистрированы, но нигде не инжектятся: адреса читаются из `IConfiguration` напрямую, а параметры
  `SocketsHttpHandler` продублированы литералами в `AddGatewayGrpcClients`. Правка этих классов ничего
  не изменит в поведении.
- `AddResilienceHandler("billing-usage", …)` внутри вызывает `services.BuildServiceProvider()` ради логгера —
  это анти-паттерн (второй контейнер), копировать в новые регистрации не надо.
- `UseLoggedExceptionHandler("/error")` включается вне Development, но **путь `/error` нигде не замаплен** —
  необработанное исключение будет залогировано, а клиент получит пустой ответ.
- Только `ChatCompletionHandler` умеет reasoning-патч и usage из стрима; image/TTS-хендлеры дублируют
  retry-цикл своим кодом. Правку общей семантики (лимиты попыток, release-статусы) нужно вносить во все три.
- YARP используется не как reverse-proxy-конвейер, а точечно — `IHttpForwarder` поверх синтетического
  `HttpContext`. Роутов/кластеров YARP в конфиге нет и не должно появиться.
