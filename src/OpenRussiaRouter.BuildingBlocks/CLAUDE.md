# BuildingBlocks — CLAUDE.md

Низ стека: деньги, токены/usage, ошибки, SQL-миграции, телеметрия, gRPC-хелперы, логирование. Подключается всеми сервисами. **Зависимостей на другие проекты solution нет.** Тащит `Microsoft.AspNetCore.App` (FrameworkReference), `Grpc.Net.Client`, `Npgsql`, Serilog/Seq, OpenTelemetry.

## Структура

| Папка | Содержимое |
|---|---|
| `Money/` | `MoneyAmount` (record struct), `Currency` |
| `Usage/` | `TokenUsage`, `ModelTokenPrice`, `UsageCostCalculator` (+`IUsageCostCalculator`), `TokenUsageExtractor` |
| `Errors/` | `ApiError`, `ErrorCodes` |
| `Migrations/` | `PostgresSqlMigrationRunner`, `SqlMigrationLoader`, `SqlMigration`, `RepositoryMigrationPath`, `WebApplicationMigrationExtensions` |
| `Grpc/` | `GrpcDeadlineInterceptor` |
| `Logging/` | `SeqLoggingExtensions` (`AddSeqLogging`, `UseLoggedExceptionHandler`) |
| `Metrics/` | `OpenTelemetryExtensions` (`AddOpenTelemetryMetrics`, `MapOpenTelemetryEndpoints` — Prometheus) |
| `Telemetry/` | `CorrelationIds` (`HeaderName` = `X-Correlation-Id` и пр.) |
| `Health/` | `ServiceHealth` |
| `Common/` | `EmailValidator` (`Normalize`), `Preconditions` (`RequireValue`) |

## Деньги — `MoneyAmount` ([файл](Money/MoneyAmount.cs))

`readonly record struct (decimal Amount, Currency Currency)`. Фабрика `Usd(amount)`. Операторы `+`/`-` бросают `InvalidOperationException` при разных валютах (`EnsureSameCurrency`). **Везде в биллинге деньги — только это, не голый `decimal`.**

## Биллинг токенов — `TokenUsage` + расчёт

- `UsageCostCalculator.Calculate(usage, price)` ([файл](Usage/UsageCostCalculator.cs)):
  - **инвариант: `InputTokens` включает `CacheTokens`** (кэш — подмножество промпта). Биллится `billableInput = Input - Cache` по input-ставке + cache по cache-ставке (защита от двойного списания, `Math.Max(0, …)` от рассинхрона);
  - формула на млн токенов, **округление до 6 знаков `MidpointRounding.ToEven`** — совпадает с `numeric(18,6)` колонок cost/ledger, чтобы возвращаемая стоимость = сохранённой.
- `TokenUsageExtractor` ([файл](Usage/TokenUsageExtractor.cs)) — извлечение usage из ответа LLM, **с нетривиальными нюансами разных провайдеров**:
  - `TryExtractJson` (non-stream) и `TryExtractStreamingSse` (берёт последний `data:`-чанк с токенами>0);
  - **9RFork раздувает usage на `BUFFER_TOKENS` (~2000)** для CLI-контекста — реальные значения дублируются в `usage._orr_raw`, и при наличии этого объекта считаются по нему;
  - алиасы полей: `input_tokens`/`prompt_tokens`, `output_tokens`/`completion_tokens`;
  - **кэш**: Anthropic (`cache_read_input_tokens`/`cache_creation_input_tokens`) репортит отдельно от input → прибавляется к input; OpenAI (`cached_tokens`, `prompt_tokens_details.cached_tokens`) уже внутри prompt → не прибавляется. `BuildUsage` нормализует оба к инварианту выше.

ApiGateway зовёт это через свою обёртку `RouterUsageExtractor`; Billing — через `UsageCostCalculator`.

## SQL-миграции

- `WebApplicationMigrationExtensions.ApplySqlMigrationsAsync(connStringName, service, migrationsDirectory)` ([файл](Migrations/WebApplicationMigrationExtensions.cs)) — сервисы зовут на старте. Гейт `SqlMigrations:Enabled` (дефолт true).
- **Грузит из директории на диске** (`SqlMigrationLoader.LoadFromDirectory`), не из embedded-ресурсов — `.sql` лежат в `db/migrations/{service}/` в корне репо. `PostgresSqlMigrationRunner.ApplyAsync` применяет по порядку (числовой префикс).
- **Во всём решении нет Entity Framework** — только сырой SQL + Dapper/Npgsql.

## gRPC / телеметрия / прочее

- `GrpcDeadlineInterceptor(defaultDeadline)` ([файл](Grpc/GrpcDeadlineInterceptor.cs)) — клиентский интерсептор: ставит дедлайн **только если он ещё не задан** на вызове. Подключать ко **всем** новым gRPC-клиентам; срок из `GrpcServices:CallDeadlineSeconds` (дефолт 30). См. память `project_grpc_deadlines`.
- `CorrelationIds.HeaderName` — не хардкодить строку `X-Correlation-Id`, брать отсюда.
- `Preconditions.RequireValue(value, name)` — стандартная not-null/not-empty проверка, используется в фасадах повсеместно.

## Правило

Сюда — только **по-настоящему общий** код (≥2 сервиса, без доменной логики конкретного сервиса). Контракты/DTO/`.proto` — в Contracts.
