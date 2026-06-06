# ApiGateway — CLAUDE.md

Публичный HTTP-вход платформы, OpenAI-совместимый API. **Только HTTP-сервер (порт 5000)** — gRPC не хостит, к Auth/Billing/ControlPlane ходит как gRPC-клиент, к нодам 9Router — напрямую HTTP. Без БД, без миграций.

## Точка входа и регистрация

`Program.cs` → `Startup.ConfigureServices` ([Startup.cs](Infrastructure/Startup/Startup.cs)) → три extension-метода из [ApiGatewayExtensions.cs](Application/Extensions/ApiGatewayExtensions.cs):
- `AddGatewayGrpcClients` — gRPC-клиенты Auth/Billing/ControlPlane (scoped провайдеры `GrpcApiKeyProvider`/`GrpcBillingProvider`/`GrpcControlPlaneProvider`), каждый с keep-alive ping и `GrpcDeadlineInterceptor` (дефолт 30с, `GrpcServices:CallDeadlineSeconds`).
- `AddGatewayRouter` — YARP (`AddReverseProxy`) + `YarpRouterProxyProvider`.
- `AddGatewayHandlers` — `GatewayPipeline`, `ChatCompletionHandler`, `ModelsHandler`.

Эндпоинты (`MapGatewayEndpoints`): `POST /v1/chat/completions`, `POST /v1/chat/completions/stream`, `GET /v1/models`, `GET /health`, `GET /ready`. Pipeline проставляет в ответ `X-Correlation-Id` и `X-Request-Id`.

## Главный флоу — два класса

### 1. `GatewayPipeline.AuthorizeAndAcquireLeaseAsync` ([GatewayPipeline.cs](Application/Services/GatewayPipeline.cs))
Авторизация одного запроса, по шагам (любой провал → пишет OpenAI-ошибку в ответ и возвращает `null`):
1. `model` не пуст;
2. `BearerTokenReader.TryRead` достаёт `sk-or-*` из `Authorization`;
3. `authProvider.AuthenticateAsync` → AuthService (UserId, ApiKeyId, ApiKeyPrefix, IsAdmin);
4. `billingProvider.CheckBalanceAndModelAsync(userId, model, adminBypassDisabled)` → Billing. Маппинг кодов ошибок в OpenAI-типы: `InsufficientBalance`→`billing_error`, `ModelUnavailable`→`invalid_request_error` и т.д.;
5. `controlPlaneProvider.SelectRouterNodeAsync(routerModelId, …, excludedNodeIds)` → ControlPlane, возвращает `GatewayRouterLease`.

**Важно: в ControlPlane передаётся `balanceResult.RouterModelId`** (upstream-ID ноды, напр. `glm-4.7`), а не публичный billing-ID (`ORR/GLM-4.7`) — иначе capability-store в ControlPlane не сматчит модель.

**Admin-байпас**: заголовок `X-ORR-Admin-Test: 1` от админ-ключа → разрешает disabled-модели (`adminBypassDisabled`).
**Preferred node**: заголовки `GatewayPreferredNodeHeaders.NodeId`/`NodeControlToken` → запрос привязывается к конкретной ноде (используется UI-тестом из WebApp).

### 2. `ChatCompletionHandler` ([ChatCompletionHandler.cs](Application/Endpoints/ChatCompletionHandler.cs))
Читает тело через `ChatCompletionRequestReader.ReadBodyAsync` (хранит и сырой JSON, и `Metadata{Model, Stream}`), по `Metadata.Stream` ветвится.

**Non-streaming (`HandleAsync`)** — цикл retry/failover:
- лиз → патч JSON → `routerProxy.SendChatCompletionAsync` (YARP) → release лиза (`completed`/`failed`);
- при `StatusCode >= 400`: нода добавляется в `excludedNodes`, `lastResponse` сохраняется, цикл повторяется на другой ноде. Когда ноды кончились (pipeline вернул `null`) — отдаётся `lastResponse`;
- при успехе: `RouterUsageExtractor.TryExtract` из тела, `RecordUsageFireAndForget`, ответ клиенту.

**Streaming (`ExecuteStreamingAsync`)** — GATEWAY-003/004:
- `routerProxy.BeginStreamChatCompletionAsync` → `RouterStreamSession` (HTTP `ResponseHeadersRead`);
- **ретрай возможен только до коммита ответа**: пока `!session.IsSuccess` — release `failed`, exclude ноды, пробуем следующую (предыдущая failed-сессия диспозится);
- после первого успешного `session` — выставляются SSE-заголовки (`text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`), и `session.CopyToAsync` стримит чанки в ответ. После коммита ошибки наружу уже не вернуть.

Патч JSON собирается из трёх шагов в [OpenAi/](OpenAi/):
- `SseRequestPatcher.PatchModel` — заменяет `model` на `routerModelId`;
- `GeminiSchemaPatcher.SanitizeIfGeminiModel` — для gemini-моделей инлайнит `$ref`/`$defs` в JSON-схемах tools/functions/response_format (Gemini не понимает `$ref`), удаляет ref-мешки; рекурсивно, с защитой от циклов (`depth>32`);
- (streaming) `SseRequestPatcher.InjectStreamOptions` — форсит `stream:true` + `stream_options.include_usage:true`, чтобы нода вернула usage в финальном чанке.

`RecordUsageFireAndForget` — `_ = billingProvider.RecordUsageAsync(...).ContinueWith(log on faulted)`. Никогда не ждём, ошибки только логируем. Статус usage: `completed` если токены>0, иначе `usage_missing`.

## Прокси к ноде ([YarpRouterProxyProvider.cs](Router/YarpRouterProxyProvider.cs))

- **Два разных HTTP-клиента (static)**: `ForwardingHttpClient` (YARP `IHttpForwarder`, non-stream) и `StreamingHttpClient` (обычный `HttpClient`, stream). Оба: `UseProxy=false`, без авто-декомпрессии, HTTP/2 multiplexing, `ConnectTimeout=15s`; `ActivityTimeout`/`Timeout=5 мин`.
- На ноду уходит `Authorization: Bearer <userApiKey>` (пользовательский `sk-or-*`, **не** control-token!) + `X-Correlation-Id`.
- Provider-метаданные снимаются из заголовков ответа ноды `x-9r-provider` / `x-9r-account` → в usage-событие.

`RouterUsageExtractor.TryExtract` ([RouterUsageExtractor.cs](Router/RouterUsageExtractor.cs)) — обёртка над общим `TokenUsageExtractor` (BuildingBlocks): сначала пробует JSON, потом SSE.

## Подводные камни

- **JSON-патчеры толерантны к мусору**: на `JsonException` возвращают исходную строку как есть — не падают.
- **`OperationCanceledException` = клиент отключился**, это норма (не ошибка), особенно для SSE.
- **Стрим-ретрай только до первого байта** — после `HasStarted` нельзя ни сменить ноду, ни отдать JSON-ошибку.
- usage-запись fire-and-forget → потеря события при краше gateway (известный пробел BILLING-004, тут только клиентская часть).
