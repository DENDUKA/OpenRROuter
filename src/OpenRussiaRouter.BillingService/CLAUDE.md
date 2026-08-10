# BillingService — CLAUDE.md

Балансовый леджер ($), каталог моделей/цен/провайдеров, usage и активность, spend-лимиты API-ключей,
платежи + FX, промо-начисления, биллинговые аномалии, теги, platform settings.
**gRPC-сервер (порт 5002)** + HTTP (`/health`, `/healthz`, `/ready`, `/`).
Layered: Domain → Application → Infrastructure.

Это **единственный владелец денег** в системе: FX-котировка, `credited_usd`, авторитетные min/max
пополнения и любые записи в леджер — только здесь. WebApp оркестрирует чекаут, но суммы не считает.

## Регистрация / bootstrap

[Program.cs](Program.cs): Seq-логи + OpenTelemetry (`DefaultMeterName` в списке метрик **обязателен**,
без него `LimitMetrics` молча не экспортируется) → `Startup.ConfigureServices` → **две пачки миграций**:

- `ApplySqlMigrationsAsync("BillingDb", "billing", …)` — `db/migrations/billing/`;
- при непустом `ProductAnalytics:ConnectionString` (и `SqlMigrations:Enabled` != `false`) — ещё и
  `db/migrations/analytics/`. **Billing — единственный сервис, накатывающий analytics-миграции**,
  хотя пишет в эту схему и WebApp (`analytics.events`).

[Startup.cs](Infrastructure/Startup/Startup.cs): `AddGrpc` с глобальным `ServiceAuthInterceptor`,
`AddControllers`, `AddHealthChecks`, `AddBillingInfrastructure(ConnectionStrings:BillingDb)`,
`AddBillingApplicationServices`, `AddProductAnalytics`, options `ExchangeRates`/`Payments`/`ServiceAuth`,
singleton `FxQuoteService` (named HTTP-клиент `BillingFx`) и hosted-сервисы —
`FxRateRefresher` и `PaymentEventRecoveryWorker`. Pipeline мапит `GrpcBillingService`, контроллеры,
`/health` и OpenTelemetry-эндпоинты.

Компакта визитов здесь больше нет: собственный трекинг трафика удалён (его считает Яндекс.Метрика),
`analytics.site_visits`/`visit_sessions` дропает `db/migrations/analytics/004_drop_site_visits.sql`.

`AddBillingInfrastructure` ([BillingServiceExtensions.cs](Application/Extensions/BillingServiceExtensions.cs)):
**пустая строка подключения → `InMemoryBillingRepository`** (так живёт dev-профиль и часть тестов).
Пагинация задаётся кодом, не конфигом: `DefaultPageSize=1000`, `MaxPageSize=5000`
([BillingPaginationOptions.cs](Application/Services/BillingPaginationOptions.cs)).

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities` | `LedgerEntry`, `ModelPrice`, `ModelProvider`, `UsageRecord`, `ActivityByTokenRecord`, `ActivitySummary` (+`ActivityModelSummary`/`ActivityKeySummary`), `AdminRequestRecord`, `ApiKeySpendLimit`, `ApiKeyLimitSummary`, `Payment`, `PaymentEvent` (+`PaymentEventInput`, `ConfirmOutcome`, `ApplyOutcome`), `BillingAnomaly`, `Tag` |
| Domain | `Domain/Enums` | `LedgerEntryType` (`VirtualTopUp`, `UsageCharge`, `Correction`, `ExternalTopUp`, `Promo1`, `FirstTopUpBonus`), `PaymentStatus` (`Created`/`Paid`/`Canceled`/`Expired`/`Refunded`/`ChargedBack`) |
| Application | `Application/Services` | `BillingApplicationService` (фасад), `IBillingRepository`, `FxQuoteService` (+`ExchangeRateOptions`, `PaymentOptions`, `FxQuote`, `FxQuoteException`), `ReasoningEffort`, `RecordUsageResult`, `BillingPaginationOptions` |
| Infrastructure | `Infrastructure/Grpc` | `GrpcBillingService` — тонкий адаптер gRPC → фасад (все деньги в proto ходят строками, парсинг/формат `InvariantCulture`) |
| Infrastructure | `Infrastructure/Auth` | `ServiceAuthInterceptor` + `ServiceAuthOptions` — s2s-защита мутирующих RPC |
| Infrastructure | `Infrastructure/Persistence` | Dapper-entity + `BillingPersistenceMapper`, `BillingDbConnectionFactory` (`NpgsqlDataSource`) |
| Infrastructure | `Infrastructure/Repository` | `BillingRepository` (PG) + `InMemoryBillingRepository` (+ `IInMemoryBillingTestHooks`) |
| Infrastructure | `Infrastructure/Workers` | `FxRateRefresher` (refresh при старте, далее раз в сутки), `PaymentEventRecoveryWorker` (батч 20 каждые 30 с) |
| Infrastructure | `Infrastructure/Http` | `HealthController` |

## Фасад — `BillingApplicationService` ([файл](Application/Services/BillingApplicationService.cs))

Деньги — только `MoneyAmount` (BuildingBlocks), валюта USD (`ValidatePositiveUsd` бракует всё остальное);
стоимость считает `IUsageCostCalculator` из BuildingBlocks. Пагинация листингов — keyset-курсор
`"{createdAtUtc:O}|{id}"` (`ParseCursor`); курсор собирает уже gRPC-слой из последнего элемента.

- **`CheckBalanceAndModelAsync(userId, modelId, adminBypassEnabled, apiKeyId, nowUtc)`** — hot-path
  для ApiGateway. Возвращает `BalanceCheckResult{Allowed, ModelId, RouterModelId, PriceVersion,
  CurrentBalance, ErrorCode, SupportsReasoning, AllowedReasoningEfforts, DefaultReasoningEffort}`:
  - модель через `GetEnabledModelAsync` (или `GetModelAsync` при admin-байпасе), нет → `ModelUnavailable`;
  - **бесплатная модель** (input/output/cache = 0) проходит при нулевом балансе и **не проверяет
    spend-лимиты вообще**; платная при `balance.Amount <= 0` → `InsufficientBalance`;
  - при переданном `apiKeyId` — суточный/недельный spend-лимит ключа → `ApiKeyLimitExceeded`;
  - отказы по балансу и лимитам пишут `LimitMetrics.RecordRejection` (`balance` / `spend_daily` /
    `spend_weekly`; при одновременном превышении в метку идёт суточный);
  - **возвращает `RouterModelId`** (upstream-id ноды) — именно его ApiGateway шлёт в ControlPlane
    и патчит в тело запроса, плюс reasoning-возможности модели.
- **`RecordUsageAsync(usageEvent, nowUtc)`**:
  - **идемпотентность по `RequestId`**: повтор → возвращает уже записанную стоимость, ничего не пишет;
  - `usage.EnsureValid()`; при невалидных токенах — аномалия `invalid_token_count` и выход;
  - при токенах > 0 резолвит `GetEnabledModelAsync`; модели нет → аномалия `model_price_missing`
    и запись с нулевой стоимостью; иначе `usageCostCalculator.Calculate`;
  - **одна транзакция `RecordUsageWithTransactionAsync(activity, usage, ledgerEntry)`**:
    `activity_by_token` + `usage_records` + списание `LedgerEntry(UsageCharge, -cost)`
    (idempotency `usage:{requestId}`). При нулевых токенах или отсутствующей цене usage/ledger = `null`
    — пишется только activity. ⚠️ Не разрывать на отдельные вызовы репозитория (комментарий в коде).
  - после записи (и на идемпотентном повторе) эмитит `ApiRequestCompleted` в продуктовую аналитику
    (fire-and-forget `Task.Run`, ошибки только логируются).
- **Леджер и пополнения**: `CreateVirtualTopUpAsync` (админское, `VirtualTopUp`), `GetBalanceAsync`,
  `ListTopUpsAsync` (по пользователю), `ListAllTopUpsAsync` (админский keyset-пейджинг по
  `(created_at_utc, id)` + фильтры по userIds и сумме; total кешируется в `_topUpsCountCache`).
- **Промо**: `GrantStartBalanceAsync` (тип `Promo1`, ключ `promo1:{userId}`, отказ при сумме `<=0`
  или `>100`, повтор возвращает существующую запись), `GrantFirstTopUpBonusAsync` (тип
  `FirstTopUpBonus`, ключ `first_top_up_bonus:{paymentId}`; требует `Payment.Status=Paid` и снапшот
  `BonusPercent`, проверяет «первое оплаченное пополнение» через
  `HasUserHadPaidTopUpAsync(userId, excludePaymentId: payment.Id)` — текущий платёж исключается по
  индексу `idempotency_key='payment:{paymentId}'`, иначе `ConfirmPaymentAtomicAsync` уже вставил
  `ExternalTopUp` с `is_user_payment=true` ДО бонуса и «первое пополнение» было бы всегда
  «не первым»; затем `UpdatePaymentBonusAsync`.
- **Каталог**: `UpsertModelAsync` требует `ModelId`/`DisplayName`/`RouterModelId`/`PriceVersion`,
  `Price.EnsureValid()`, нормализует reasoning-efforts через `ReasoningEffort`
  (канон: `none|minimal|low|medium|high|xhigh`; при `SupportsReasoning=false` списки должны быть
  пустыми; `DefaultReasoningEffort` обязан входить в `AllowedReasoningEfforts`), резолвит
  `ProviderId` в существующего `ModelProvider` (иначе `ArgumentException`) и подставляет его
  `DisplayName` в `Provider`.
- **Провайдеры моделей**: `UpsertModelProviderAsync`/`GetModelProviderAsync`/`GetModelProviderLogoAsync`/
  `DeleteModelProviderAsync`. Id ≤ 50 (`TagIdRegex`), displayName ≤ 200, логотип — только
  `image/png|jpeg|webp|svg+xml` и ≤ 256 KiB; id всегда `ToLowerInvariant()` при чтении/удалении.
- **Теги**: `UpsertTagAsync`/`DeleteTagAsync` — id по `TagIdRegex` и ≤ 50, displayName ≤ 100,
  цвет по `HexColorRegex` (`#rgb`/`#rrggbb`).
- **Платежи**: `GetFxQuote`, `CreatePaymentAsync`, `AttachProviderRefAsync`,
  `ConfirmPaymentByProviderAsync`, `ApplyPaymentEventAsync`, `GetPaymentAsync`, `ListPaymentsAsync`,
  `AcquireStalePaymentsAsync`. Ошибки — `PaymentOperationException(ErrorCode)` и
  `FxQuoteException(ErrorCode)`, gRPC-слой превращает их в `error_code` в ответе. Успешный
  `ConfirmPaymentByProviderAsync` эмитит `PaymentCompleted` в продуктовую аналитику.
- **Platform settings**: `GetPlatformSettingAsync`/`SetPlatformSettingAsync` — generic key/value.
  Промо-настройки читаются gRPC-методом `GetPromotionSettings` прямо по ключам
  `promotions.start_balance.enabled|amount_usd`, `promotions.first_top_up.enabled|percent`.
- Листинги активности/usage/аномалий/admin-записей — с фильтрами по apiKeyIds, датам, моделям,
  провайдерам, нодам и аккаунтам (`GetAdminUsageRecordsAsync` / `GetAdminUsageSummaryAsync`).

## Деньги: инварианты

- **Баланс = `billing.user_balances`** — денормализованная таблица `user_balances(user_id PK, balance_amount numeric(18,6))`, синхронизируемая при каждом изменении леджера. Чтение — `O(1)`, запись — атомарный `UPDATE ... SET balance = balance + amount WHERE balance + amount >= 0` внутри транзакции списания (T-028). Кеша нет. Леджер append-only, глобальный unique-индекс на `idempotency_key`. Идемпотентность на уровне репозитория (T-069): ledger insert идёт ПЕРВЫМ через CTE с `ON CONFLICT DO NOTHING`; `user_balances` обновляется ТОЛЬКО если insert реально вставил строку (cross-join + `EXISTS`). Двойной вызов с одним `requestId` дебетует баланс ровно один раз.
- Ключи идемпотентности: `usage:{requestId}`, `payment:{paymentId}`, `refund:{paymentId}`,
  `chargeback:{paymentId}`, `refund_bonus:{paymentId}`, `promo1:{userId}`,
  `first_top_up_bonus:{paymentId}`; админское пополнение — ключ от вызывающего.
- Все денежные колонки — `numeric(18,6)`; **любой расчёт денег в C# округляется через единый
  `MoneyRounding.Round` (BuildingBlocks/Money, `ToEven`, 6 знаков)** — стоимость usage, `credited_usd`
  и `bonus_usd`. Bankers' rounding совпадает с PostgreSQL scale-coercion `numeric(18,6)`/`numeric(12,6)`,
  поэтому сохранённое значение = посчитанному; никаких inline `decimal.Round(..., AwayFromZero)`.
  В частности, `bonus_usd` округляется **в C# до** `UpdatePaymentBonusAsync`, иначе леджер и
  `payments.bonus_usd` разъезжаются в 6-м знаке (T-033).
- Предварительная проверка — только `balance > 0`, а не «хватит на запрос»: **за один вызов баланс
  может уйти в минус**, это осознанный компромисс.
- **`credited_usd` считается только по своей котировке** (`FxQuoteService.GetQuote` → `usdPerUnit`),
  затем сверяется с `PaymentOptions.MinTopUpUsd/MaxTopUpUsd`. Поддерживаются `RUB`/`USD`/`EUR`,
  всё остальное — `UnsupportedCurrency`.
- Подтверждение платежа (`ConfirmPaymentAtomicAsync`): `update … where status='Created' and
  provider_amount=@ConfirmedAmount and provider_currency=@ConfirmedCurrency` → только при успехе
  вставляется `ExternalTopUp` с `is_user_payment=true`. Расхождение суммы/валюты → аномалия
  `payment_amount_mismatch` + `PaymentAmountMismatch`, повторный вебхук на уже `Paid` — возврат
  существующего `ledger_entry_id`.
- `refunded` всегда сторнируется (`Correction`, `-credited_usd`) вместе с бонусом первого пополнения;
  `chargeback` — только при `Payments:AutoReverseChargeback=true`.

## s2s-аутентификация мутирующих RPC

[ServiceAuthInterceptor.cs](Infrastructure/Auth/ServiceAuthInterceptor.cs) вешается на **все**
gRPC-вызовы, но проверяет лишь список мутирующих методов, **зашитый в коде** (`ProtectedMethods`):
`RecordUsage`, `CreateVirtualTopUp`, `UpsertApiKeyLimitSettings`, `UpsertModelPrice`, `UpsertTag`,
`DeleteTag`, `CreatePayment`, `AttachProviderRef`, `ConfirmPaymentByProvider`, `ApplyPaymentEvent`,
`AcquireStalePayments`, `UpsertModelProvider`, `DeleteModelProvider`,
**`GrantStartBalance`, `GrantFirstTopUpBonus`, `SetPlatformSetting`** (последние три — «печатает
деньги»/правит настройки; T-002). Читающие методы (`GetBalance`, `ListPublicModels`, `GetFxQuote`,
`HasUserHadPaidTopUp`, `GetPromotionSettings`, …) открыты намеренно. Клиент присылает
`x-orr-caller` + `x-orr-s2s-token`; токен ищется в `ServiceAuth:Tokens` и сравнивается
constant-time, затем caller проверяется по `ServiceAuth:MethodPolicies[method]`. Отсутствующая
политика для защищённого метода — **fail closed** (`Unauthenticated`,
`reason=missing_method_policy`). Фактические caller-id: `apigateway` (только `RecordUsage`)
и `webappadmin` (всё остальное, включая `Grant*` и `SetPlatformSetting`).

**Fail-fast на старте** — [`ServiceAuthStartupGuard`](Infrastructure/Auth/ServiceAuthStartupGuard.cs)
(T-003): вне `Development` падает `InvalidOperationException`, если `ServiceAuth:Tokens[apigateway]`
или `[webappadmin]` пустые/whitespace. Шитый `appsettings.json` держит оба токена **пустыми** —
 реальные секреты приходят только env (`ServiceAuth__Tokens__<caller>`). Dev-заглушки живут в
 `appsettings.Development.json` и работают только при `ASPNETCORE_ENVIRONMENT=Development`.
 По образцу `PaymentStartupGuard`; ни в коем случае не ослаблять — иначе интерцептору не с чем
  сравнивать токен, и любой, кто дотянется до порта 5002, обходят s2s полностью.

## Схема БД (`db/migrations/billing/`, 25 файлов)

| Таблица | Что |
|---|---|
| `billing.ledger_entries` | append-only леджер; unique `idempotency_key`; `is_user_payment` (023) отделяет реальные платежи от промо; check `currency='Usd'` (010) |
| `billing.model_prices` | одна строка на `model_id` + unique-индекс на `router_model_id` (008); `description_ru`/`description_en` (022, старый `description` удалён), `tag_ids`, `provider`/`provider_id`, `model_type` (019, check `in ('text','audio')`), `priority` (016), `supports_reasoning`/`allowed_reasoning_efforts` (CSV)/`default_reasoning_effort` (014), `input_modalities`/`output_modalities` (016) |
| `billing.usage_records` | строка на каждый запрос, у которого нашлась цена и токены > 0 (в т.ч. с нулевой стоимостью у бесплатных моделей), PK `request_id`; источник правды для spend-лимитов и админских отчётов |
| `billing.activity_by_token` | то же в разрезе API-ключа для кабинета (004); пишется **всегда**, даже когда usage/ledger пропущены |
| `billing.billing_anomalies` | `invalid_token_count`, `model_price_missing`, `payment_amount_mismatch` |
| `billing.api_key_spend_limits` | **PK = `api_key_id`** (не пара с `user_id`), лимиты `> 0` или NULL (017) |
| `billing.payments` | статусы через check-constraint, лиза (`lease_owner`/`lease_until_utc`), unique `(provider, provider_payment_id)` и `(user_id, client_token)`, `bonus_percent`/`bonus_usd` (016) |
| `billing.payment_events` | сырые события провайдера, `processed`/`attempts`, unique `(provider, provider_event_id)` либо `(payment_id, event_type)` |
| `billing.tags` (011), `billing.model_providers` (020), `billing.platform_settings` (015) | справочники и key/value-настройки |
| `billing.user_balances` (026) | денормализованный баланс (`user_id PK`, `balance_amount numeric(18,6)`); синхронизируется при каждом INSERT в `ledger_entries` (CTE в `AddLedgerEntryAsync`) и атомарно дебетируется в `RecordUsageWithTransactionAsync` (T-028) |

`UpsertModelAsync` мержит описания хитро: перезаписывает описания/контекст/модальности только когда
пришёл `description_source`, иначе `COALESCE` — ручные правки не затираются авто-обогащением.

## Конфигурация

| Ключ | Смысл |
|---|---|
| `ConnectionStrings:BillingDb` | пусто → in-memory репозиторий (dev/тесты) |
| `Payments:MinTopUpUsd` / `MaxTopUpUsd` | авторитетные границы пополнения в **$** после конвертации |
| `Payments:AutoReverseChargeback` | сторнировать ли chargeback автоматически (по умолчанию `false`) |
| `ExchangeRates:Source` / `MarkupPercent` / `Fallback:RubPerUsd` / `Fallback:RubPerEur` | FX; `MarkupPercent` уменьшает `usdPerUnit` (наценка в пользу платформы) |
| `ServiceAuth:Tokens` / `ServiceAuth:MethodPolicies` | s2s (см. выше) |
| `ProductAnalytics:*` | продуктовая аналитика; непустой `ConnectionString` дополнительно включает накат analytics-миграций |
| `SqlMigrations:Enabled` | `false` отключает накат analytics-миграций |

## Подводные камни

- **`appsettings*.json` попадают в publish только благодаря ручным `<Content Include>`** в
  `.csproj` — проект на `Microsoft.NET.Sdk`, а не `Sdk.Web`. Уберёшь — в образе не окажется
  `ServiceAuth`, и `RecordUsage` начнёт падать с `missing_method_policy` (это уже случалось,
  см. комментарий в csproj).
- **Новый мутирующий RPC = четыре правки**: `.proto`, `ProtectedMethods` в интерцепторе,
  `MethodPolicies` во всех окружениях (appsettings + compose/kustomize env — но окружения
  переопределяют только `Tokens`, политики берутся из `appsettings.json`), и Theory-кейс в
  `GrpcBillingServiceIntegrationTests` (чтобы protection проверялся на CI). Пропустишь третье —
  метод закроется наглухо (`missing_method_policy`); забудешь второй — метод останется открытым.
  Регистрация нового RPC, пишущего деньги или настройки, **должна** попасть в `ProtectedMethods`.
- **`ServiceAuthStartupGuard` падает вне `Development` при пустом `apigateway`/`webappadmin`
  токене** (T-003). Шитый `appsettings.json` держит оба токена пустыми — это намеренно, секреты
  приходят только env. `ServiceAuthPolicyCoverageTests` (`NoPolicyTargetsAnUnprotectedMethod` +
  `EveryProtectedMethod_HasAPolicyInShippedAppSettings` + `ShippedAppSettings_DoesNotSeedAnyRealDefaultToken`)
  ловит рассинхрон `ProtectedMethods` и конфига и рецидив закоммиченного секрета.
  Не возвращать `appsettings.json` к непустому токену «по умолчанию» — это открывает прямой обход
  s2s в любом окружении без env-переменной (T-002+T-003).
- **`FxQuoteService`**: static-конструктор регистрирует `CodePagesEncodingProvider` — ЦБ отдаёт
  XML в `windows-1251`, без этого `RefreshAsync` тихо падает в warning и все платежи считаются по
  fallback-курсу (`fx_source=fallback`). Снапшот живёт в памяти singleton'а: после рестарта до
  первого успешного refresh — снова fallback. Котировка старше **48 ч** → `FxQuoteUnavailable`.
- **Один active-row на модель** в `billing.model_prices` — versioned price history нет (BILLING-003).
  `price_version` пишется в usage для аудита, но прошлые цены не сохраняются.
- **`InsertPaymentEventAsync` пишет событие отдельным соединением, до транзакции применения** —
  именно поэтому существует `PaymentEventRecoveryWorker`: он добирает `processed=false`
  (`for update skip locked`, батч 20, раз в 30 с) и доводит побочные эффекты до конца. Воркер обязан передавать `Payments:AutoReverseChargeback` в `AcquireUnprocessedEventsAsync`, иначе recovery-путь расходится с прямой обработкой вебхука.
- **Spend-лимиты считаются по `usage_records`**: сутки — от 00:00 UTC, неделя — от понедельника UTC
  (`GetUtcDayStart`/`GetUtcWeekStart`). Превышение — `spent >= limit`. Бесплатные модели лимиты
  не проверяют.
- `admin_bypass_disabled` в proto означает «админу разрешено дёрнуть **выключенную** модель»
  (в фасаде параметр называется `adminBypassEnabled`) — читается наоборот, чем кажется.
- Repository pattern с двумя реализациями — **новый метод в обе** (PG + in-memory).
  `InMemoryBillingRepository.ConfirmPaymentAtomicAsync` обязан ставить `IsUserPayment=true` на
  `ExternalTopUp`-записи (как `BillingRepository.InsertLedgerAsync`), иначе `HasUserHadPaidTopUpAsync`
  и тесты «первого пополнения» расходятся с PG.
- Префикс `016` в миграциях занят **тремя** файлами; сортировка — по полному имени файла
  (`SqlMigrationLoader`), так что это работает, но следующий файл нужно называть `027_…`.
  Применённые миграции неизменяемы.
- **Select-лист `billing.payments` — общий через `PaymentSelectColumns`** (см. `BillingRepository.cs`).
  `PaymentSelectSql` (для `from billing.payments`) и `PaymentSelectFromUpdatedSql` (для CTE
  `from updated` в `AcquireStalePaymentsAsync`) собраны из него конкатенацией. **При добавлении
  новой колонки в `billing.payments`** поправить надо ровно один `PaymentSelectColumns` и
  `PaymentEntity` + маппер — иначе Dapper молча оставит `null` и бонус/снапшот потеряется
  (так T-029 словил регрессию: `AcquireStalePaymentsAsync` обрывал список на `canceled_at_utc`
  и не возвращал `bonus_percent`/`bonus_usd`, реконсилиляция браковала платёж `no_bonus_snapshot`).

## Тесты

`tests/OpenRussiaRouter.BillingService.Tests` (проект виден через `InternalsVisibleTo`):

- `Application/BillingApplicationServiceTests` — фасад на in-memory репозитории;
- `Application/FxQuoteServiceTests`, `PaymentApplicationServiceTests`, `PaymentRecoveryTests`,
  `ProductAnalyticsEmissionTests`;
- `Application/GrpcBillingServiceTests` / `GrpcBillingServiceIntegrationTests` /
  `GrpcBillingServicePostgresIntegrationTests` — поднимают хост целиком, s2s-заголовки берут из
  `S2sTestTokens` и **переопределяют `ServiceAuthOptions` в DI** (значения не обязаны совпадать
  с appsettings);
- `Infrastructure/Persistence/BillingRepositoryPostgresTests` + `PostgresTestDatabase` — Testcontainers
  (`postgres:16-alpine`), без Docker `StartAsync` возвращает `null` и тесты скипаются.
  Строку подключения можно переопределить `ORR_BILLING_POSTGRES_TEST_CONNECTION`.
