# BuildingBlocks — CLAUDE.md

Низ стека: деньги, токены/usage, SQL-миграции, телеметрия и метрики, gRPC-интерсепторы,
логирование, продуктовая аналитика. Подключается всеми сервисами.
**Зависимостей на другие проекты solution нет** — и не появляется: если типу нужен
`Contracts`, он не в BuildingBlocks. Тащит `Microsoft.AspNetCore.App` (FrameworkReference),
`Grpc.Net.Client`, `Npgsql` (+`Npgsql.OpenTelemetry`), Serilog/Seq, OpenTelemetry
(AspNetCore/Http/Runtime/Process + Prometheus-экспортер).

Тесты — `tests/OpenRussiaRouter.BuildingBlocks.Tests`, папки зеркалят структуру ниже
(`Analytics`, `Common`, `Migrations`, `Money`, `Reasoning`, `Usage`).

## Структура

| Папка | Что там | Ключевые типы |
|---|---|---|
| `Money/` | доллар как единственная валюта | `MoneyAmount`, `Currency` (`Usd = 840`, других значений нет), `MoneyRounding` (единая политика округления денег) |
| `Usage/` | токены, цены, расчёт стоимости, парсинг usage из ответов LLM | `TokenUsage`, `ModelTokenPrice`, `UsageCostCalculator` (+`IUsageCostCalculator`), `TokenUsageExtractor` |
| `Migrations/` | применение сырых `.sql` на старте сервиса | `PostgresSqlMigrationRunner`, `SqlMigrationLoader`, `SqlMigration`, `RepositoryMigrationPath`, `WebApplicationMigrationExtensions` |
| `Analytics/` | продуктовые события: очередь и воркер | `IProductAnalytics`, `AnalyticsEventBuilder`, `AnalyticsEvents`, `AnalyticsBuckets`, `UserPseudonymizer`, `ProductAnalyticsEventQueue`/`FlushWorker`, `IProductAnalyticsErasure` |
| `Metrics/` | Prometheus-экспорт и общий счётчик лимитов | `OpenTelemetryExtensions` (`AddOpenTelemetryMetrics`, `MapOpenTelemetryEndpoints`, `DefaultMeterName`), `LimitMetrics` |
| `Grpc/` | клиентские и серверные интерсепторы | `GrpcDeadlineInterceptor`, `ServiceAuthClientInterceptor`, `ServiceAuthInterceptor` (серверный, конфигурируется через `ServiceAuthServerOptions.ProtectedMethods` per-service) |
| `Logging/` | Serilog + Seq, логирование необработанных исключений | `SeqLoggingExtensions` (`AddSeqLogging`, `UseLoggedExceptionHandler`) |
| `Telemetry/` | correlation id | `CorrelationIds` |
| `Reasoning/` | перевод reasoning-опций ноды 9Router в канон ORR | `NodeReasoningMapper` |
| `Errors/` | строковые коды ошибок платформы | `ErrorCodes` |
| `Health/` | тело ответа health-эндпоинта | `ServiceHealth.Ok(service)` |
| `Common/` | микро-хелперы валидации | `EmailValidator.Normalize`, `Preconditions.RequireValue`/`RequireNotNull` |

## Деньги — `MoneyAmount` ([файл](Money/MoneyAmount.cs))

`readonly record struct (decimal Amount, Currency Currency)`. Фабрика `Usd(amount)`.
Операторы `+`/`-` бросают `InvalidOperationException` при разных валютах (`EnsureSameCurrency`),
`*`/`/` не определены. **Везде в биллинге деньги — только это, не голый `decimal`.**

### Округление — `MoneyRounding` ([файл](Money/MoneyRounding.cs))

Единая политика округления денег для всего solution: `decimal.Round(..., 6, MidpointRounding.ToEven)`
через `MoneyRounding.Round(decimal)` / `MoneyRounding.Round(MoneyAmount)`. `DecimalPlaces = 6`,
`Mode = ToEven`. Bankers' rounding совпадает с тем, как `numeric(18,6)`/`numeric(12,6)` скейлят
значение в PostgreSQL, поэтому C#- side и БД не расходятся в 6-м знаке. **Любой расчёт денег, который
уходит в БД (стоимость usage, `credited_usd`, `bonus_usd`), обязан округляться через этот хелпер** —
никаких inline `decimal.Round(..., AwayFromZero)` и `Math.Round` в сервисах быть не должно.

## Биллинг токенов — `TokenUsage` + расчёт

- `UsageCostCalculator.Calculate(usage, price)` ([файл](Usage/UsageCostCalculator.cs)):
  - **инвариант: `InputTokens` включает `CacheTokens`** (кэш — подмножество промпта).
    Отсюда же `TokenUsage.TotalTokens = Input + Output` (кэш не прибавляется второй раз).
    Биллится `billableInput = Input - Cache` по input-ставке + cache по cache-ставке
    (защита от двойного списания, `Math.Max(0, …)` от рассинхрона);
  - формула на млн токенов, **округление до 6 знаков через `MoneyRounding.Round`**
    (`MidpointRounding.ToEven`) — совпадает с `numeric(18,6)` колонок cost/ledger, чтобы
    возвращаемая стоимость = сохранённой;
  - `EnsureValid()` на обоих аргументах: отрицательные токены и отрицательные ставки —
    исключение, все три ставки в `ModelTokenPrice` обязаны быть в одной валюте.
- `TokenUsageExtractor` ([файл](Usage/TokenUsageExtractor.cs)) — извлечение usage из ответа LLM,
  **с нетривиальными нюансами разных провайдеров**:
  - `TryExtractJson` (non-stream) и `TryExtractStreamingSse` (идёт по всем `data:`-строкам,
    `[DONE]` пропускает, оставляет последний чанк с `TotalTokens > 0`);
  - `"usage": null` в чанке (MiniMax-M3) — **обязательная проверка `ValueKind == Object`**:
    без неё `TryGetProperty` по Null-элементу бросает `InvalidOperationException`, который
    не ловится `catch (JsonException)` и вылезает 500-кой;
  - **9RFork раздувает usage на `BUFFER_TOKENS` (~2000)** для CLI-контекста — реальные значения
    дублируются в `usage._orr_raw`, и при наличии этого объекта считаются по нему;
  - алиасы полей: `input_tokens`/`prompt_tokens`, `output_tokens`/`completion_tokens`;
  - **кэш**: Anthropic (`cache_read_input_tokens`/`cache_creation_input_tokens`) репортит отдельно
    от input → прибавляется к input; OpenAI (`cache_tokens`/`cached_tokens`, вложенные
    `prompt_tokens_details.cached_tokens` и `input_tokens_details.cached_tokens`) уже внутри
    prompt → не прибавляется. `BuildUsage` нормализует оба к инварианту выше.

ApiGateway зовёт это через свою обёртку `RouterUsageExtractor`; Billing — через `UsageCostCalculator`.

## SQL-миграции

- `WebApplicationMigrationExtensions.ApplySqlMigrationsAsync(connStringName, service, migrationsDirectory)`
  ([файл](Migrations/WebApplicationMigrationExtensions.cs)) — сервисы зовут в `Program.cs` сразу
  после `builder.Build()`. Гейт `SqlMigrations:Enabled` (дефолт true), строка подключения — из
  `ConnectionStrings:{connStringName}`.
- **Грузит из директории на диске** (`SqlMigrationLoader.LoadFromDirectory`), не из embedded-ресурсов:
  только `*.sql` верхнего уровня, сортировка по имени файла (`OrdinalIgnoreCase`),
  `SqlMigration.Version` = имя файла без расширения.
- `RepositoryMigrationPath.ForService(service)` сначала пробует `{AppContext.BaseDirectory}/db/migrations/{service}`
  (в контейнере: Dockerfile делает `COPY --from=build /src/db ./db`), иначе идёт вверх до каталога
  с `OpenRussiaRouter.sln` (локальный запуск).
- `PostgresSqlMigrationRunner.ApplyAsync` ([файл](Migrations/PostgresSqlMigrationRunner.cs)):
  **сам создаёт БД, если её нет** (коннектится к `postgres`, `create database`), поднимает
  `public.schema_migrations (service, version, applied_at_utc)` и применяет каждую миграцию
  в **своей транзакции** с проверкой «уже применена». Отсюда правило неизменяемости файлов:
  применённую миграцию не редактируем — она больше не выполнится.
- **`analytics`-схему миграциями катает BillingService**, отдельным вызовом в своём `Program.cs`
  по строке `ProductAnalytics:ConnectionString` (если она задана) — не через `ApplySqlMigrationsAsync`.
- **Во всём решении нет Entity Framework** — только сырой SQL + Dapper/Npgsql.

## Продуктовая аналитика (`Analytics/`)

Конвейер: сервис зовёт `IProductAnalytics` → `AnalyticsEventBuilder` фильтрует и псевдонимизирует →
`ProductAnalyticsEventQueue` (bounded channel) → `ProductAnalyticsFlushWorker` батчами пишет
в `analytics.events`.

- **Fail-open по всей длине.** Методы `IProductAnalytics` — `void` и **никогда не бросают**;
  очередь создана с `BoundedChannelFullMode.DropWrite` (переполнение = событие потеряно + warning);
  воркер логирует ошибку и **дропает батч**, хост не останавливает. Аналитика не имеет права
  ломать бизнес-путь.
- **Приватность — два барьера в `AnalyticsEvents`** ([файл](Analytics/AnalyticsEvents.cs)):
  1. **default-deny allow-list** на каждое событие — экспортируется только ключ, который явно
     разрешён для этого события;
  2. **глобальный deny-list** (`email`, `prompt`, `content`, `token`, `api_key`, `authorization`,
     `cookie` и пр., сравнение case-insensitive) как defense-in-depth. В **DEBUG** утечка
     запрещённого ключа — `InvalidOperationException`, в **Release** ключ молча выкидывается.
  Плюс билдер сам подставляет `schema_version` (`AnalyticsEvents.SchemaVersion`, сейчас `"2"`)
  и `environment`. **Добавил/убрал свойство в allow-list — подними `SchemaVersion`.**
  Неизвестное имя события → исключение, зарегистрировать его нужно в `CallerSchemas`.
- **Точных чисел в события не попадает**: `AnalyticsBuckets` заменяет токены, суммы, латентность
  и HTTP-код на грубые метки (`1K-10K`, `500ms-1s`, `4xx`, …). Это структурная гарантия, а не
  соглашение — в `AnalyticsEvent.Props` уходит только бакет.
- **Псевдонимизация**: `UserPseudonymizer.ComputeDistinctId` = `Base64Url(HMAC-SHA256(IdentitySalt, userId))`.
  Это **единственный санкционированный HMAC-примитив аналитики** — пароли, API-ключи и
  session-токены живут на других примитивах в своих сервисах. Ротация соли обнуляет связь
  со старыми `distinct_id`.
- **Дедупликация**: `ApiRequestAnalytics.DedupKey`/`PaymentAnalytics.DedupKey` (id usage-записи,
  id платежа) едут **отдельной колонкой**, не внутри `properties`; вставка идёт с
  `on conflict (event_name, dedup_key) where dedup_key is not null do nothing`.
- **Стирание** — `IProductAnalyticsErasure.PurgeUserAsync(userId)`: `delete from analytics.events`
  по вычисленному `distinct_id`, синхронно и полностью. Реализации `Postgres` и `NoOp`.
- **Регистрация** — `services.AddProductAnalytics(configuration)` (секция `ProductAnalytics`).
  Postgres-путь включается только при `Enabled=true` **и** `Provider="Postgres"` **и** непустом
  `ConnectionString`; иначе — `NoOpProductAnalytics`/`NoOpProductAnalyticsErasure`.
  Дефолт `Provider` — `"PostHog"`, и **внешнего PostHog-синка в коде нет** (зарезервировано);
  валидатор требует `IdentitySalt`/`HostUrl`/`Environment` только на этом легаси-пути
  (`Enabled` + непустой `ProjectToken`), чтобы dev и тесты не падали на старте.
  Подключено в `AuthService`, `BillingService` (через их `Startup`) и `WebApp`.
- **Подводный камень DI**: `AddProductAnalytics` регистрирует `NpgsqlDataSource` как синглтон —
  это **датасорс аналитической БД**. Свою БД сервисы открывают через собственные
  `{Service}DbConnectionFactory`, а не через `NpgsqlDataSource` из контейнера. Не резолвить его
  для сервисной БД и не регистрировать второй.

## Трафик сайта — здесь его больше нет

Собственный трекинг визитов (`SiteVisit*`, `VisitorIdHasher`, `VisitSession*`) **удалён**:
трафик сайта считает Яндекс.Метрика, счётчик живёт в layout WebApp. Вместе с кодом ушли
`analytics.site_visits`/`visit_sessions` (дропает `db/migrations/analytics/004_drop_site_visits.sql`),
компакт в BillingService и админские страницы `/admin/visitors` и `/admin/sessions`.
Не заводить заново — дублировать Метрику своим трекером незачем. `analytics.events` выше
это не затрагивает: продуктовые события живут дальше.

## Метрики

- `AddOpenTelemetryMetrics(serviceName, params additionalMeters)` + `MapOpenTelemetryEndpoints()`
  ([файл](Metrics/OpenTelemetryExtensions.cs)): AspNetCore/HttpClient/Runtime/Process-инструментация
  + Prometheus scraping endpoint.
  **Кастомные инструменты не экспортируются, если не передать `OpenTelemetryExtensions.DefaultMeterName`**
  (`"OpenRussiaRouter"`) в `additionalMeters` — молча, без ошибки. Сервисам с Postgres добавляют ещё `"Npgsql"`.
- `LimitMetrics.RecordRejection(limitType, subject)` ([файл](Metrics/LimitMetrics.cs)) — единый
  счётчик отказов по лимитам, в Prometheus `orr_limit_rejections_total`. Значения меток —
  только из `LimitMetrics.LimitTypes` (`rate_limit`, `spend_daily`, `spend_weekly`, `balance`)
  и `LimitMetrics.Subjects` (`api_key`, `ip`, `user`).
  **В лейблы нельзя класть api key id / user id / ip** — неограниченная кардинальность на
  публичном эндпоинте. Разрезы «кто именно упёрся» берём SQL-панелями поверх
  `billing.usage_records`, не метриками. Пишут сюда `GatewayRateLimiting` (ApiGateway) и
  `BillingApplicationService`.

## gRPC-интерсепторы (оба — клиентские)

- `GrpcDeadlineInterceptor(defaultDeadline)` ([файл](Grpc/GrpcDeadlineInterceptor.cs)) — ставит
  дедлайн **только если он ещё не задан** на вызове. Подключать ко **всем** новым gRPC-клиентам;
  срок из `GrpcServices:CallDeadlineSeconds` (дефолт 30).
- `ServiceAuthClientInterceptor(callerId, token)` ([файл](Grpc/ServiceAuthClientInterceptor.cs)) —
  s2s-аутентификация: копирует существующие заголовки в новую `Metadata` (не мутирует вызывающего)
  и добавляет `x-orr-caller` + `x-orr-s2s-token`. Ставится **поверх** deadline-интерсептора:
  в ApiGateway — на клиенте Billing, в WebApp — на клиентах Billing и Content.
- `ServiceAuthInterceptor` ([файл](Grpc/ServiceAuthInterceptor.cs)) — серверная половина s2s-аутентификации.
  Конфигурируется через `ServiceAuthOptions` (токены и политики из `appsettings.json`) и
  `ServiceAuthServerOptions` (набор защищённых методов, определяется per-service). Каждый сервис
  регистрирует свой `ProtectedMethods` через `services.Configure<ServiceAuthServerOptions>(o => o.ProtectedMethods = ...)`.

## Логирование, correlation id, прочее

- `AddSeqLogging(defaultServiceName)` ([файл](Logging/SeqLoggingExtensions.cs)) — Serilog вместо
  дефолтного логгера: конфиг из `appsettings` + DI, `Service` = `Seq:ServiceName` либо аргумент,
  консоль всегда, Seq — если задан `Seq:Url`. **Логи запросов к `/health*` отфильтрованы**,
  не искать их в Seq. `UseLoggedExceptionHandler(errorPath)` логирует необработанное исключение
  (и то, что уже подобрал `IExceptionHandlerFeature`) до того, как хендлер отдаст ответ.
- `CorrelationIds.HeaderName` ([файл](Telemetry/CorrelationIds.cs)) — константа **в нижнем регистре**
  (`x-correlation-id`), плюс `New()` (`Guid` в формате `"N"`). Не хардкодить строку, брать отсюда.
  Чтение из запроса — в `ApiGateway/Http/CorrelationIdReader.cs`, не здесь.
- `NodeReasoningMapper` ([файл](Reasoning/NodeReasoningMapper.cs)) — переводит `caps.thinkingOptions`
  ноды 9Router в канон ORR `OrderedCanonical` = `none,minimal,low,medium,high,xhigh,max,ultra,thinking`:
  `on`→`high`, `off`→`none`, `auto` **выбрасывается** (канонического аналога нет), результат
  дедуплицирован и упорядочен low→high. `PickDefaultEffort` предпочитает `medium` > `high` > `low`
  > первый доступный. Потребители — админка WebApp (`Admin/Models`, `Admin/ModelsSync`) и
  `ReasoningEnrichmentWorker`; результат пишется прямо в `allowed_reasoning_efforts`, поэтому
  сырые токены ноды туда **нельзя** без прогонки через маппер.
- `ErrorCodes` ([файл](Errors/ErrorCodes.cs)) — только строковые константы (auth, баланс, лимиты,
  роутер, платежи/FX). **Формирование тела ошибки в OpenAI-формате здесь не живёт** — это
  `ApiGateway/Http/GatewayErrors.cs`.
- `Preconditions.RequireValue(value, name)` — стандартная not-null/not-empty проверка,
  **возвращает `Trim()`-нутое значение** (используется в фасадах повсеместно);
  `RequireNotNull<T>` — для ссылочных типов. `EmailValidator.Normalize` = trim + lowercase +
  требование `@`.
- `ServiceHealth.Ok(service)` — тело ответа `HealthController` во всех gRPC-сервисах.

## Правило

Сюда — только **по-настоящему общий** код (≥2 сервиса, без доменной логики конкретного сервиса).
Контракты/DTO/`.proto` — в Contracts.
