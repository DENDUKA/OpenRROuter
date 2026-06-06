# BillingService — CLAUDE.md

Балансовый леджер ($), каталог моделей и цен, запись/агрегация usage, биллинговые аномалии, теги. **gRPC-сервер (порт 5002)** + HTTP `/health`. Layered: Domain → Application → Infrastructure.

## Регистрация

`Startup.ConfigureServices`: `AddGrpc` + `AddControllers` + `AddBillingInfrastructure(ConnectionStrings:BillingDb)` + `AddBillingApplicationServices`. Мапит `GrpcBillingService` и `/health`.

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities` | `LedgerEntry`, `ModelPrice`, `UsageRecord`, `ActivityByTokenRecord`, `ActivitySummary`, `AdminRequestRecord`, `BillingAnomaly`, `Tag` |
| Domain | `Domain/Enums` | `LedgerEntryType` (`VirtualTopUp`, `UsageCharge`, …) |
| Application | `Application/Services` | `BillingApplicationService` (фасад, `partial`), `IBillingRepository`, `RecordUsageResult`, `BillingPaginationOptions` |
| Infrastructure | `Infrastructure/Grpc` | `GrpcBillingService` |
| Infrastructure | `Infrastructure/Persistence` | Dapper-entity + `BillingPersistenceMapper`, `BillingDbConnectionFactory` |
| Infrastructure | `Infrastructure/Repository` | `BillingRepository` (PG) + `InMemoryBillingRepository` |

## Фасад — `BillingApplicationService` ([файл](Application/Services/BillingApplicationService.cs))

Деньги — только `MoneyAmount` (BuildingBlocks), валюта USD; расчёт стоимости делегируется `IUsageCostCalculator`. Пагинация — keyset-курсор `"{createdAtUtc:O}|{id}"` (`ParseCursor`), размер из `BillingPaginationOptions.EffectivePageSize`.

- **`CheckBalanceAndModelAsync(userId, modelId, adminBypassDisabled)`** — главный hot-path для ApiGateway. Возвращает `BalanceCheckResult{Allowed, RouterModelId, PriceVersion, Balance, ErrorCode}`:
  - модель ищется через `GetEnabledModelAsync` (или `GetModelAsync` при admin-байпасе), нет → `ModelUnavailable`;
  - **бесплатная модель** (input/output/cache цены = 0) проходит при нулевом балансе; платная с `balance.Amount <= 0` → `InsufficientBalance`;
  - **возвращает `RouterModelId`** (upstream-ID ноды) — именно его ApiGateway шлёт в ControlPlane и патчит в запрос.
- **`RecordUsageAsync(usageEvent, nowUtc)`** — запись usage:
  - **идемпотентность по `RequestId`**: повтор → возвращает уже записанную стоимость, не дублирует;
  - `usage.EnsureValid()`; при невалидных токенах пишет `BillingAnomaly("invalid_token_count")` и выходит;
  - если токены>0 — резолв `GetEnabledModelAsync`; модели нет → аномалия `model_price_missing` + usage с нулевой стоимостью; иначе `usageCostCalculator.Calculate`;
  - **одна транзакция `RecordUsageWithTransactionAsync(activity, usage, ledgerEntry)`**: пишет `ActivityByTokenRecord` + `UsageRecord` + списание `LedgerEntry(UsageCharge, -cost)` (idempotency `usage:{requestId}`). При нулевой стоимости usage/ledger = `null` (только activity).
  - ⚠️ В коде есть комментарий-предупреждение: транзакционность реализована на уровне репозитория — не разрывать на отдельные вызовы.
- **`CreateVirtualTopUpAsync`** — админское пополнение (`ValidatePositiveUsd`, idempotencyKey).
- **`UpsertModelAsync`** — каталог; требует `ModelId`/`DisplayName`/`RouterModelId`/`PriceVersion`, `Price.EnsureValid()`.
- **Теги** (`UpsertTagAsync`/`DeleteTagAsync`): валидация id (`TagIdRegex`, ≤50), displayName (≤100), цвет (`HexColorRegex`).
- Списки активности/usage/аномалий/admin-записей — с фильтрами по apiKeyIds, датам, модели, провайдеру.

## Подводные камни / известные пробелы

- **Один active-row на модель** в `billing.model_prices` — нет versioned price history (BILLING-003). `PriceVersion` хранится в usage для аудита, но прошлые версии цен не сохраняются.
- **Usage приходит fire-and-forget** от ApiGateway по in-process gRPC — нет PG-backed outbox (BILLING-004), при потере вызова событие теряется.
- Repository pattern с двумя реализациями — **новый метод в обе** (PG + in-memory).

## БД и тесты

Миграции `db/migrations/billing/` (5 файлов). Тесты: unit + integration + PG. Override — `ORR_BILLING_POSTGRES_TEST_CONNECTION`.
