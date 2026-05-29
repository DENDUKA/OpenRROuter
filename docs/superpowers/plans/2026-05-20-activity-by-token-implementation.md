# План реализации ActivityByToken

> **Для agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Шаги используют checkbox-синтаксис (`- [ ]`) для отслеживания выполнения.

**Цель:** Сохранять и показывать активность запросов по каждому персональному API-токену для всех запросов, которые дошли до 9Router-ноды, без хранения полного bearer token.

**Архитектура:** AuthService возвращает префикс API key при валидации. ApiGateway прокидывает `api_key_id` и `api_key_prefix` в BillingService. BillingService пишет отдельную read-model таблицу `billing.activity_by_token` до денежной части, а WebApp Activity читает эту таблицу вместо billing usage records.

**Стек:** .NET 8, gRPC/protobuf, Dapper, PostgreSQL migrations, Razor Pages, xUnit.

---

## Структура файлов

- Изменить `src/OpenRussiaRouter.Contracts/Protos/auth.proto`: добавить `api_key_prefix` в `ValidateApiKeyResponse`.
- Изменить `src/OpenRussiaRouter.Contracts/Protos/billing.proto`: добавить `api_key_prefix` в `RecordUsageRequest`, добавить RPC и сообщения для `ListActivityByToken`.
- Изменить `src/OpenRussiaRouter.Contracts/Auth/AuthContracts.cs`: добавить `ApiKeyPrefix` в `ValidateApiKeyResult`.
- Изменить `src/OpenRussiaRouter.Contracts/Billing/BillingContracts.cs`: добавить `ApiKeyPrefix` в `UsageEvent`.
- Изменить `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs`: возвращать prefix в validation response.
- Изменить `src/OpenRussiaRouter.AuthService/Application/Services/InMemoryAuthApiKeyValidator.cs`: возвращать prefix для repository-ключей и seeded keys.
- Изменить `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthApiKeyValidator.cs`: выбирать и возвращать сохраненный prefix.
- Изменить `src/OpenRussiaRouter.ApiGateway/Auth/ApiKeyAuthenticationResult.cs`: передавать prefix.
- Изменить `src/OpenRussiaRouter.ApiGateway/Auth/GrpcApiKeyAuthenticator.cs`: маппить prefix из AuthService response.
- Изменить `src/OpenRussiaRouter.ApiGateway/Billing/GatewayUsageEvent.cs`: передавать prefix.
- Изменить `src/OpenRussiaRouter.ApiGateway/Billing/GrpcBillingGatewayClient.cs`: отправлять prefix в BillingService.
- Изменить `src/OpenRussiaRouter.ApiGateway/Router/RouterUsageExtractor.cs`: нормализовать nested cache token fields и безопасно обрабатывать malformed JSON.
- Изменить `src/OpenRussiaRouter.ApiGateway/Infrastructure/Startup/Startup.cs`: всегда писать post-node activity, используя `usage_missing`, если usage отсутствует.
- Создать `src/OpenRussiaRouter.BillingService/Domain/Entities/ActivityByTokenRecord.cs`: domain row для read-model.
- Создать `src/OpenRussiaRouter.BillingService/Infrastructure/Persistence/ActivityByTokenEntity.cs`: Dapper entity.
- Изменить `src/OpenRussiaRouter.BillingService/Infrastructure/Persistence/BillingPersistenceMapper.cs`: добавить маппинг activity rows.
- Изменить `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs`: добавить методы записи/чтения activity.
- Изменить `src/OpenRussiaRouter.BillingService/Application/Services/InMemoryBillingRepository.cs`: хранить activity rows для тестов.
- Изменить `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs`: insert/list для `activity_by_token`.
- Изменить `src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs`: писать activity до billing charge logic.
- Изменить `src/OpenRussiaRouter.BillingService/Application/Services/GrpcBillingService.cs`: замаппить новый prefix и endpoint списка activity.
- Создать `db/migrations/billing/004_activity_by_token.sql`: таблица PostgreSQL и индексы.
- Создать `src/OpenRussiaRouter.WebApp/Services/Billing/ActivityByTokenInfo.cs`: DTO для WebApp.
- Изменить `src/OpenRussiaRouter.WebApp/Services/Billing/BillingClient.cs`: добавить `ListActivityByTokenAsync`.
- Изменить `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml.cs`: загружать ключи пользователя и activity rows.
- Изменить `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml`: показывать token prefix и убрать колонку cost.
- Изменить тесты в `tests/OpenRussiaRouter.AuthService.Tests`, `tests/OpenRussiaRouter.ApiGateway.Tests`, `tests/OpenRussiaRouter.BillingService.Tests`.

---

### Задача 1: AuthService возвращает API key prefix

**Файлы:**
- Изменить: `src/OpenRussiaRouter.Contracts/Protos/auth.proto`
- Изменить: `src/OpenRussiaRouter.Contracts/Auth/AuthContracts.cs`
- Изменить: `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs`
- Изменить: `src/OpenRussiaRouter.AuthService/Application/Services/InMemoryAuthApiKeyValidator.cs`
- Изменить: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthApiKeyValidator.cs`
- Тест: `tests/OpenRussiaRouter.AuthService.Tests/Application/GrpcAuthServiceTests.cs`
- Тест: `tests/OpenRussiaRouter.AuthService.Tests/Application/InMemoryAuthApiKeyValidatorTests.cs`

- [ ] **Шаг 1: Написать падающие AuthService tests**

Добавить/обновить assertions в `GrpcAuthServiceTests.ValidateApiKey_ValidKey_ReturnsGrpcResponse`:

```csharp
new StubValidator(new ValidateApiKeyResult(true, "user-1", "key-1", "sk-or-abc123", null))
```

```csharp
Assert.Equal("sk-or-abc123", response.ApiKeyPrefix);
```

Добавить seed-key validator test в `InMemoryAuthApiKeyValidatorTests`:

```csharp
[Fact]
public void Validate_SeedKey_ReturnsPrefixSnapshot()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:SeedApiKeys:0:RawKey"] = "sk-or-seeded-secret",
            ["Auth:SeedApiKeys:0:UserId"] = "user-seed",
            ["Auth:SeedApiKeys:0:ApiKeyId"] = "key-seed"
        })
        .Build();
    var validator = new InMemoryAuthApiKeyValidator(new Sha256SecretHasher(), configuration);

    var result = validator.Validate("sk-or-seeded-secret");

    Assert.True(result.IsValid);
    Assert.Equal("sk-or-seed", result.ApiKeyPrefix);
}
```

- [ ] **Шаг 2: Запустить AuthService tests и убедиться, что они падают**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests -nr:false -m:1 --filter "GrpcAuthServiceTests|InMemoryAuthApiKeyValidatorTests"
```

Ожидаемо: FAIL, потому что `ValidateApiKeyResult` и generated `ValidateApiKeyResponse` еще не имеют `ApiKeyPrefix`.

- [ ] **Шаг 3: Добавить prefix в auth proto и contracts**

В `auth.proto` обновить `ValidateApiKeyResponse`:

```proto
message ValidateApiKeyResponse {
  bool is_valid = 1;
  string user_id = 2;
  string api_key_id = 3;
  string error_code = 4;
  string api_key_prefix = 5;
}
```

В `AuthContracts.cs` обновить record:

```csharp
public sealed record ValidateApiKeyResult(
    bool IsValid,
    string? UserId,
    string? ApiKeyId,
    string? ApiKeyPrefix,
    string? ErrorCode);
```

- [ ] **Шаг 4: Вернуть prefix из validators**

В `AuthApiKeyValidator.cs` добавить prefix в query и row:

```csharp
ak.prefix as ApiKeyPrefix,
```

```csharp
return new ValidateApiKeyResult(true, row.UserId, row.ApiKeyId, row.ApiKeyPrefix, null);
```

```csharp
private static ValidateApiKeyResult Invalid()
{
    return new ValidateApiKeyResult(false, null, null, null, ErrorCodes.InvalidApiKey);
}

private sealed record ApiKeyValidationRow(string ApiKeyId, string UserId, string ApiKeyPrefix);
```

В `InMemoryAuthApiKeyValidator.cs` добавить общий helper для prefix:

```csharp
private const int VisiblePrefixLength = 12;

private static string GetPrefix(string rawKey)
{
    return rawKey[..Math.Min(rawKey.Length, VisiblePrefixLength)];
}
```

Вернуть repository prefixes:

```csharp
return new ValidateApiKeyResult(true, apiKey.UserId, apiKey.Id, apiKey.Prefix, null);
```

Сохранять seeded prefixes:

```csharp
public void RegisterRawKey(string rawKey, string userId, string apiKeyId)
{
    var key = new StoredApiKey(userId, apiKeyId, GetPrefix(rawKey), Revoked: false);
    _keysByHash[_secretHasher.Hash(rawKey)] = key;
}
```

```csharp
return new ValidateApiKeyResult(true, key.UserId, key.ApiKeyId, key.ApiKeyPrefix, null);
```

```csharp
private sealed record StoredApiKey(string UserId, string ApiKeyId, string ApiKeyPrefix, bool Revoked);
```

- [ ] **Шаг 5: Замаппить prefix в GrpcAuthService**

Установить:

```csharp
ApiKeyPrefix = result.ApiKeyPrefix ?? string.Empty,
```

в `ValidateApiKey`.

- [ ] **Шаг 6: Запустить AuthService tests**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests -nr:false -m:1
```

Ожидаемо: PASS.

- [ ] **Шаг 7: Commit Auth prefix changes**

```powershell
git add src/OpenRussiaRouter.Contracts/Protos/auth.proto src/OpenRussiaRouter.Contracts/Auth/AuthContracts.cs src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs src/OpenRussiaRouter.AuthService/Application/Services/InMemoryAuthApiKeyValidator.cs src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthApiKeyValidator.cs tests/OpenRussiaRouter.AuthService.Tests/Application/GrpcAuthServiceTests.cs tests/OpenRussiaRouter.AuthService.Tests/Application/InMemoryAuthApiKeyValidatorTests.cs
git commit -m "feat: return api key prefix from auth validation"
```

---

### Задача 2: Добавить Billing ActivityByToken contract и domain

**Файлы:**
- Изменить: `src/OpenRussiaRouter.Contracts/Protos/billing.proto`
- Изменить: `src/OpenRussiaRouter.Contracts/Billing/BillingContracts.cs`
- Создать: `src/OpenRussiaRouter.BillingService/Domain/Entities/ActivityByTokenRecord.cs`
- Создать: `src/OpenRussiaRouter.BillingService/Infrastructure/Persistence/ActivityByTokenEntity.cs`
- Изменить: `src/OpenRussiaRouter.BillingService/Infrastructure/Persistence/BillingPersistenceMapper.cs`

- [ ] **Шаг 1: Расширить billing proto**

Добавить RPC:

```proto
rpc ListActivityByToken (ListActivityByTokenRequest) returns (ListActivityByTokenResponse);
```

Добавить `api_key_prefix` в `RecordUsageRequest`:

```proto
string api_key_prefix = 10;
```

Добавить messages:

```proto
message ListActivityByTokenRequest {
  repeated string api_key_ids = 1;
}

message ActivityByTokenRecord {
  string request_id = 1;
  string api_key_id = 2;
  string api_key_prefix = 3;
  string model_id = 4;
  string router_node_id = 5;
  TokenUsage usage = 6;
  string status = 7;
  string created_at_utc = 8;
  string completed_at_utc = 9;
}

message ListActivityByTokenResponse {
  repeated ActivityByTokenRecord records = 1;
}
```

- [ ] **Шаг 2: Добавить prefix в UsageEvent**

Обновить `BillingContracts.cs`:

```csharp
public sealed record UsageEvent(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
```

- [ ] **Шаг 3: Добавить domain и persistence records**

Создать `ActivityByTokenRecord.cs`:

```csharp
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record ActivityByTokenRecord(
    string RequestId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);
```

Создать `ActivityByTokenEntity.cs`:

```csharp
namespace OpenRussiaRouter.BillingService.Infrastructure.Persistence;

public sealed class ActivityByTokenEntity
{
    public string RequestId { get; set; } = string.Empty;
    public string ApiKeyId { get; set; } = string.Empty;
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string RouterNodeId { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheTokens { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
```

- [ ] **Шаг 4: Добавить mapper methods**

Добавить в `BillingPersistenceMapper.cs`:

```csharp
public static ActivityByTokenEntity ToEntity(ActivityByTokenRecord record)
{
    return new ActivityByTokenEntity
    {
        RequestId = record.RequestId,
        ApiKeyId = record.ApiKeyId,
        ApiKeyPrefix = record.ApiKeyPrefix,
        ModelId = record.ModelId,
        RouterNodeId = record.RouterNodeId,
        InputTokens = record.Usage.InputTokens,
        OutputTokens = record.Usage.OutputTokens,
        CacheTokens = record.Usage.CacheTokens,
        Status = record.Status,
        CreatedAtUtc = record.CreatedAtUtc,
        CompletedAtUtc = record.CompletedAtUtc
    };
}

public static ActivityByTokenRecord ToDomain(ActivityByTokenEntity entity)
{
    return new ActivityByTokenRecord(
        entity.RequestId,
        entity.ApiKeyId,
        entity.ApiKeyPrefix,
        entity.ModelId,
        entity.RouterNodeId,
        new TokenUsage(entity.InputTokens, entity.OutputTokens, entity.CacheTokens),
        entity.Status,
        entity.CreatedAtUtc,
        entity.CompletedAtUtc);
}
```

- [ ] **Шаг 5: Собрать проект, чтобы проверить generated gRPC types**

Команда:

```powershell
dotnet build OpenRussiaRouter.sln -nr:false -m:1
```

Ожидаемо: build errors только в местах, которые еще используют старый `UsageEvent` constructor. Эти ошибки закрываются следующими задачами.

---

### Задача 3: Добавить Activity persistence в BillingService

**Файлы:**
- Создать: `db/migrations/billing/004_activity_by_token.sql`
- Изменить: `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs`
- Изменить: `src/OpenRussiaRouter.BillingService/Application/Services/InMemoryBillingRepository.cs`
- Изменить: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs`
- Тест: `tests/OpenRussiaRouter.BillingService.Tests/Application/BillingApplicationServiceTests.cs`

- [ ] **Шаг 1: Написать падающие BillingService application tests**

Добавить tests:

```csharp
[Fact]
public void RecordUsage_ValidUsage_WritesActivityByToken()
{
    var service = CreateServiceWithModel();
    service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

    service.RecordUsage(CreateUsageEvent("req-activity", new TokenUsage(10, 20, 3)), DateTimeOffset.UtcNow);

    var activity = Assert.Single(service.ListActivityByToken(["key-1"]));
    Assert.Equal("req-activity", activity.RequestId);
    Assert.Equal("key-1", activity.ApiKeyId);
    Assert.Equal("sk-or-test", activity.ApiKeyPrefix);
    Assert.Equal(10, activity.Usage.InputTokens);
    Assert.Equal(20, activity.Usage.OutputTokens);
    Assert.Equal(3, activity.Usage.CacheTokens);
}

[Fact]
public void RecordUsage_UsageMissing_WritesActivityWithoutCharge()
{
    var service = CreateServiceWithModel();
    service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);

    var result = service.RecordUsage(CreateUsageEvent("req-missing", new TokenUsage(0, 0, 0), "usage_missing"), DateTimeOffset.UtcNow);

    Assert.True(result.Accepted);
    Assert.Equal(MoneyAmount.Usd(100), service.GetBalance("user-1"));
    var activity = Assert.Single(service.ListActivityByToken(["key-1"]));
    Assert.Equal("usage_missing", activity.Status);
    Assert.Equal(0, activity.Usage.TotalTokens);
}

[Fact]
public void RecordUsage_SameRequestId_DoesNotDuplicateActivity()
{
    var service = CreateServiceWithModel();
    service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Usd(100), "test", "topup-1", DateTimeOffset.UtcNow);
    var usage = CreateUsageEvent("req-idempotent", new TokenUsage(1, 1, 0));

    service.RecordUsage(usage, DateTimeOffset.UtcNow);
    service.RecordUsage(usage, DateTimeOffset.UtcNow);

    Assert.Single(service.ListActivityByToken(["key-1"]));
}
```

Обновить helper:

```csharp
private static UsageEvent CreateUsageEvent(string requestId, TokenUsage usage, string status = "succeeded")
{
    var now = DateTimeOffset.UtcNow;
    return new UsageEvent(
        requestId,
        "user-1",
        "key-1",
        "sk-or-test",
        "gpt-test",
        "router-1",
        usage,
        status,
        now.AddSeconds(-1),
        now);
}
```

- [ ] **Шаг 2: Запустить BillingService application tests и убедиться, что они падают**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.BillingService.Tests -nr:false -m:1 --filter BillingApplicationServiceTests
```

Ожидаемо: FAIL, потому что repository и service еще не имеют `ListActivityByToken`.

- [ ] **Шаг 3: Добавить PostgreSQL migration**

Создать `004_activity_by_token.sql`:

```sql
create table if not exists billing.activity_by_token (
    request_id varchar(128) primary key,
    api_key_id varchar(64) not null,
    api_key_prefix varchar(32) not null,
    model_id varchar(128) not null,
    router_node_id varchar(64) not null,
    input_tokens bigint not null,
    output_tokens bigint not null,
    cache_tokens bigint not null,
    status varchar(32) not null,
    created_at_utc timestamptz not null,
    completed_at_utc timestamptz not null
);

create index if not exists ix_billing_activity_by_token_key_created
    on billing.activity_by_token (api_key_id, created_at_utc desc);

create index if not exists ix_billing_activity_by_token_created
    on billing.activity_by_token (created_at_utc desc);
```

- [ ] **Шаг 4: Добавить repository methods**

В `IBillingRepository.cs`:

```csharp
ActivityByTokenRecord AddActivityByToken(ActivityByTokenRecord record);

(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore) ListActivityByToken(
    IReadOnlyCollection<string> apiKeyIds,
    int limit,
    DateTimeOffset? afterCreatedAtUtc = null);
```

В `InMemoryBillingRepository.cs` добавить dictionary:

```csharp
private readonly ConcurrentDictionary<string, ActivityByTokenRecord> _activityByRequestId = new(StringComparer.Ordinal);
```

Добавить methods:

```csharp
public ActivityByTokenRecord AddActivityByToken(ActivityByTokenRecord record)
{
    ArgumentNullException.ThrowIfNull(record);
    return _activityByRequestId.GetOrAdd(record.RequestId, record);
}

public (IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore) ListActivityByToken(
    IReadOnlyCollection<string> apiKeyIds,
    int limit,
    DateTimeOffset? afterCreatedAtUtc = null)
{
    var keySet = apiKeyIds.ToHashSet(StringComparer.Ordinal);
    var items = _activityByRequestId.Values
        .Where(item => keySet.Contains(item.ApiKeyId))
        .Where(item => afterCreatedAtUtc is null || item.CreatedAtUtc > afterCreatedAtUtc)
        .OrderByDescending(item => item.CreatedAtUtc)
        .Take(limit + 1)
        .ToArray();

    var hasMore = items.Length > limit;
    if (hasMore)
    {
        items = items.Take(limit).ToArray();
    }

    return (items, hasMore);
}
```

- [ ] **Шаг 5: Добавить Dapper persistence**

В `BillingRepository.cs` добавить insert/list methods:

```csharp
public ActivityByTokenRecord AddActivityByToken(ActivityByTokenRecord record)
{
    ArgumentNullException.ThrowIfNull(record);
    using var connection = connectionFactory.CreateConnection();
    connection.Execute(
        """
        insert into billing.activity_by_token
            (request_id, api_key_id, api_key_prefix, model_id, router_node_id,
             input_tokens, output_tokens, cache_tokens, status, created_at_utc, completed_at_utc)
        values
            (@RequestId, @ApiKeyId, @ApiKeyPrefix, @ModelId, @RouterNodeId,
             @InputTokens, @OutputTokens, @CacheTokens, @Status, @CreatedAtUtc, @CompletedAtUtc)
        on conflict (request_id) do nothing;
        """,
        new
        {
            record.RequestId,
            record.ApiKeyId,
            record.ApiKeyPrefix,
            record.ModelId,
            record.RouterNodeId,
            record.Usage.InputTokens,
            record.Usage.OutputTokens,
            record.Usage.CacheTokens,
            record.Status,
            record.CreatedAtUtc,
            record.CompletedAtUtc
        });

    return BillingPersistenceMapper.ToDomain(connection.QuerySingle<ActivityByTokenEntity>(
        ActivityByTokenByRequestSql,
        new { record.RequestId }));
}
```

```csharp
public (IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore) ListActivityByToken(
    IReadOnlyCollection<string> apiKeyIds,
    int limit,
    DateTimeOffset? afterCreatedAtUtc = null)
{
    if (apiKeyIds.Count == 0)
    {
        return (Array.Empty<ActivityByTokenRecord>(), false);
    }

    using var connection = connectionFactory.CreateConnection();
    var items = connection.Query<ActivityByTokenEntity>(
            ActivityByTokenSelectSql + """
            where api_key_id = any(@ApiKeyIds)
              and (@AfterCreatedAtUtc is null or created_at_utc > @AfterCreatedAtUtc)
            order by created_at_utc desc
            limit @Limit;
            """,
            new { ApiKeyIds = apiKeyIds.ToArray(), Limit = limit + 1, AfterCreatedAtUtc = afterCreatedAtUtc })
        .Select(BillingPersistenceMapper.ToDomain)
        .ToArray();

    var hasMore = items.Length > limit;
    if (hasMore)
    {
        items = items.Take(limit).ToArray();
    }

    return (items, hasMore);
}
```

Добавить SQL constants:

```csharp
private const string ActivityByTokenSelectSql = """
    select
        request_id as RequestId,
        api_key_id as ApiKeyId,
        api_key_prefix as ApiKeyPrefix,
        model_id as ModelId,
        router_node_id as RouterNodeId,
        input_tokens as InputTokens,
        output_tokens as OutputTokens,
        cache_tokens as CacheTokens,
        status as Status,
        created_at_utc as CreatedAtUtc,
        completed_at_utc as CompletedAtUtc
    from billing.activity_by_token

    """;

private const string ActivityByTokenByRequestSql = ActivityByTokenSelectSql + """
    where request_id = @RequestId;
    """;
```

- [ ] **Шаг 6: Писать activity до money path**

В `BillingApplicationService.RecordUsage`, после успешного `usageEvent.Usage.EnsureValid()`, добавить:

```csharp
repository.AddActivityByToken(new ActivityByTokenRecord(
    usageEvent.RequestId,
    usageEvent.ApiKeyId,
    usageEvent.ApiKeyPrefix,
    usageEvent.ModelId,
    usageEvent.RouterNodeId,
    usageEvent.Usage,
    usageEvent.Status,
    nowUtc,
    usageEvent.CompletedAtUtc));

if (usageEvent.Usage.TotalTokens == 0)
{
    return new RecordUsageResult(true, usageEvent.RequestId, MoneyAmount.Usd(0), null);
}
```

Добавить public method:

```csharp
public IReadOnlyCollection<ActivityByTokenRecord> ListActivityByToken(IReadOnlyCollection<string> apiKeyIds)
{
    if (apiKeyIds.Count == 0)
    {
        return Array.Empty<ActivityByTokenRecord>();
    }

    var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
    return repository.ListActivityByToken(apiKeyIds, pageSize).Items;
}
```

- [ ] **Шаг 7: Запустить BillingService application tests**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.BillingService.Tests -nr:false -m:1 --filter BillingApplicationServiceTests
```

Ожидаемо: PASS.

---

### Задача 4: Отдать ActivityByToken через Billing gRPC

**Файлы:**
- Изменить: `src/OpenRussiaRouter.BillingService/Application/Services/GrpcBillingService.cs`
- Тест: `tests/OpenRussiaRouter.BillingService.Tests/Application/GrpcBillingServiceTests.cs`

- [ ] **Шаг 1: Написать падающие gRPC tests**

Добавить:

```csharp
[Fact]
public async Task ListActivityByToken_ReturnsOnlyRequestedKeys()
{
    var billing = CreateBillingWithModelAndBalance();
    var service = new GrpcBillingService(billing);
    var now = DateTimeOffset.UtcNow;

    _ = await service.RecordUsage(new RecordUsageRequest
    {
        RequestId = "req-key-1",
        UserId = "user-1",
        ApiKeyId = "key-1",
        ApiKeyPrefix = "sk-or-key-1",
        ModelId = "gpt-test",
        RouterNodeId = "router-1",
        Usage = new Contracts.Grpc.Billing.V1.TokenUsage { InputTokens = 10, OutputTokens = 20, CacheTokens = 3 },
        Status = "completed",
        StartedAtUtc = now.AddSeconds(-1).ToString("O"),
        CompletedAtUtc = now.ToString("O")
    }, context: null!);

    var response = await service.ListActivityByToken(new ListActivityByTokenRequest
    {
        ApiKeyIds = { "key-1" }
    }, context: null!);

    var record = Assert.Single(response.Records);
    Assert.Equal("req-key-1", record.RequestId);
    Assert.Equal("key-1", record.ApiKeyId);
    Assert.Equal("sk-or-key-1", record.ApiKeyPrefix);
    Assert.Equal(10, record.Usage.InputTokens);
    Assert.Equal(20, record.Usage.OutputTokens);
    Assert.Equal(3, record.Usage.CacheTokens);
}
```

- [ ] **Шаг 2: Запустить gRPC tests и убедиться, что они падают**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.BillingService.Tests -nr:false -m:1 --filter GrpcBillingServiceTests
```

Ожидаемо: FAIL, пока `GrpcBillingService` не маппит новое поле request и endpoint.

- [ ] **Шаг 3: Замаппить `api_key_prefix` в RecordUsage**

Обновить construction `UsageEvent`:

```csharp
var usageEvent = new UsageEvent(
    request.RequestId,
    request.UserId,
    request.ApiKeyId,
    request.ApiKeyPrefix,
    request.ModelId,
    request.RouterNodeId,
    new BuildingBlocks.Usage.TokenUsage(
        request.Usage?.InputTokens ?? 0,
        request.Usage?.OutputTokens ?? 0,
        request.Usage?.CacheTokens ?? 0),
    request.Status,
    ParseTimestamp(request.StartedAtUtc),
    ParseTimestamp(request.CompletedAtUtc));
```

- [ ] **Шаг 4: Добавить `ListActivityByToken` gRPC method**

Добавить:

```csharp
public override Task<ListActivityByTokenResponse> ListActivityByToken(
    ListActivityByTokenRequest request,
    ServerCallContext context)
{
    var response = new ListActivityByTokenResponse();
    response.Records.AddRange(billing.ListActivityByToken(request.ApiKeyIds.ToArray()).Select(activity => new Contracts.Grpc.Billing.V1.ActivityByTokenRecord
    {
        RequestId = activity.RequestId,
        ApiKeyId = activity.ApiKeyId,
        ApiKeyPrefix = activity.ApiKeyPrefix,
        ModelId = activity.ModelId,
        RouterNodeId = activity.RouterNodeId,
        Usage = new Contracts.Grpc.Billing.V1.TokenUsage
        {
            InputTokens = activity.Usage.InputTokens,
            OutputTokens = activity.Usage.OutputTokens,
            CacheTokens = activity.Usage.CacheTokens
        },
        Status = activity.Status,
        CreatedAtUtc = activity.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        CompletedAtUtc = activity.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)
    }));

    return Task.FromResult(response);
}
```

- [ ] **Шаг 5: Запустить BillingService tests**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.BillingService.Tests -nr:false -m:1
```

Ожидаемо: PASS.

- [ ] **Шаг 6: Commit BillingService activity changes**

```powershell
git add src/OpenRussiaRouter.Contracts/Protos/billing.proto src/OpenRussiaRouter.Contracts/Billing/BillingContracts.cs src/OpenRussiaRouter.BillingService db/migrations/billing/004_activity_by_token.sql tests/OpenRussiaRouter.BillingService.Tests
git commit -m "feat: persist activity by api token"
```

---

### Задача 5: Пронести prefix и нормализовать usage в ApiGateway

**Файлы:**
- Изменить: `src/OpenRussiaRouter.ApiGateway/Auth/ApiKeyAuthenticationResult.cs`
- Изменить: `src/OpenRussiaRouter.ApiGateway/Auth/GrpcApiKeyAuthenticator.cs`
- Изменить: `src/OpenRussiaRouter.ApiGateway/Billing/GatewayUsageEvent.cs`
- Изменить: `src/OpenRussiaRouter.ApiGateway/Billing/GrpcBillingGatewayClient.cs`
- Изменить: `src/OpenRussiaRouter.ApiGateway/Router/RouterUsageExtractor.cs`
- Изменить: `src/OpenRussiaRouter.ApiGateway/Infrastructure/Startup/Startup.cs`
- Тест: `tests/OpenRussiaRouter.ApiGateway.Tests/Router/RouterUsageExtractorTests.cs`
- Тест: `tests/OpenRussiaRouter.ApiGateway.Tests/Integration/ChatCompletionsIntegrationTests.cs`

- [ ] **Шаг 1: Написать падающие gateway tests**

В `RouterUsageExtractorTests` добавить:

```csharp
[Fact]
public void TryExtract_NestedPromptDetailsCache_ReturnsCacheTokens()
{
    var json = """{"usage":{"prompt_tokens":10,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":7}}}""";

    var result = RouterUsageExtractor.TryExtract(json, out var usage);

    Assert.True(result);
    Assert.Equal(10, usage.InputTokens);
    Assert.Equal(20, usage.OutputTokens);
    Assert.Equal(7, usage.CacheTokens);
}

[Fact]
public void TryExtract_InputTokenDetailsCache_ReturnsCacheTokens()
{
    var json = """{"usage":{"input_tokens":10,"output_tokens":20,"input_tokens_details":{"cached_tokens":8}}}""";

    var result = RouterUsageExtractor.TryExtract(json, out var usage);

    Assert.True(result);
    Assert.Equal(8, usage.CacheTokens);
}

[Fact]
public void TryExtract_MalformedJson_ReturnsFalse()
{
    var result = RouterUsageExtractor.TryExtract("""{"usage":""", out var usage);

    Assert.False(result);
    Assert.Equal(default, usage);
}
```

В `ChatCompletionsIntegrationTests` обновить `StubAuthenticator` default:

```csharp
public ApiKeyAuthenticationResult Result { get; set; } =
    ApiKeyAuthenticationResult.Success("user-1", "key-1", "sk-or-test");
```

Добавить tests:

```csharp
[Fact]
public async Task NonStreaming_HappyPath_UsageEventContainsApiKeyPrefix()
{
    const string routerBody = """{"id":"chatcmpl-1","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
    var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
    var billing = new StubBillingClient();
    var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

    await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

    await Task.Delay(50);
    Assert.NotNull(billing.LastUsageEvent);
    Assert.Equal("sk-or-test", billing.LastUsageEvent.ApiKeyPrefix);
}

[Fact]
public async Task NonStreaming_RouterBodyWithoutUsage_RecordsUsageMissingActivity()
{
    const string routerBody = """{"id":"chatcmpl-1","choices":[{"message":{"content":"Hi"}}]}""";
    var router = new StubRouterProxy { Response = new RouterProxyResponse(200, "application/json", routerBody) };
    var billing = new StubBillingClient();
    var client = CreateClient(new StubAuthenticator(), billing, new StubControlPlane(), router);

    await PostChatAsync(client, """{"model":"gpt-4"}""", bearer: "sk-ok_key");

    await Task.Delay(50);
    Assert.NotNull(billing.LastUsageEvent);
    Assert.Equal("usage_missing", billing.LastUsageEvent.Status);
    Assert.Equal(0, billing.LastUsageEvent.Usage.TotalTokens);
}
```

- [ ] **Шаг 2: Запустить gateway tests и убедиться, что они падают**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.ApiGateway.Tests -nr:false -m:1 --filter "RouterUsageExtractorTests|ChatCompletionsIntegrationTests"
```

Ожидаемо: FAIL, пока gateway не прокидывает prefix и не пишет missing usage.

- [ ] **Шаг 3: Пронести prefix через auth и billing DTOs**

Обновить `ApiKeyAuthenticationResult`:

```csharp
public sealed record ApiKeyAuthenticationResult(
    bool Succeeded,
    string? UserId,
    string? ApiKeyId,
    string? ApiKeyPrefix,
    string? ErrorCode)
{
    public static ApiKeyAuthenticationResult Success(string userId, string apiKeyId, string apiKeyPrefix)
    {
        return new ApiKeyAuthenticationResult(true, userId, apiKeyId, apiKeyPrefix, null);
    }

    public static ApiKeyAuthenticationResult Failed(string errorCode)
    {
        return new ApiKeyAuthenticationResult(false, null, null, null, errorCode);
    }
}
```

Обновить `GrpcApiKeyAuthenticator`:

```csharp
return ApiKeyAuthenticationResult.Success(response.UserId, response.ApiKeyId, response.ApiKeyPrefix);
```

Обновить `GatewayUsageEvent`:

```csharp
public sealed record GatewayUsageEvent(
    string RequestId,
    string UserId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    TokenUsage Usage,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
```

Обновить `GrpcBillingGatewayClient.RecordUsageAsync`:

```csharp
ApiKeyPrefix = usageEvent.ApiKeyPrefix,
```

- [ ] **Шаг 4: Нормализовать nested usage fields**

Обновить `RouterUsageExtractor.TryExtract`, чтобы ловить parse failures и читать nested cache fields:

```csharp
public static bool TryExtract(string json, out TokenUsage usage)
{
    usage = default;
    if (string.IsNullOrWhiteSpace(json))
    {
        return false;
    }

    try
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("usage", out var usageElement))
        {
            return false;
        }

        usage = new TokenUsage(
            ReadInt64(usageElement, "input_tokens", "prompt_tokens"),
            ReadInt64(usageElement, "output_tokens", "completion_tokens"),
            ReadCacheTokens(usageElement));
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}
```

Добавить helpers:

```csharp
private static long ReadCacheTokens(JsonElement usageElement)
{
    var direct = ReadInt64(usageElement, "cache_tokens", "cached_tokens", "cache_read_input_tokens");
    if (direct > 0)
    {
        return direct;
    }

    return ReadNestedInt64(usageElement, "prompt_tokens_details", "cached_tokens")
        ?? ReadNestedInt64(usageElement, "input_tokens_details", "cached_tokens")
        ?? 0;
}

private static long? ReadNestedInt64(JsonElement element, string objectName, string propertyName)
{
    if (element.TryGetProperty(objectName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object &&
        nested.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt64(out var value))
    {
        return value;
    }

    return null;
}
```

- [ ] **Шаг 5: Всегда писать activity после ответа ноды**

В non-streaming `Startup.HandleChatCompletionsAsync` заменить guarded block `if (usage.InputTokens > 0 || usage.OutputTokens > 0)` на:

```csharp
var hasUsage = RouterUsageExtractor.TryExtract(response.Body, out var usage)
    && usage.TotalTokens > 0;
var status = response.StatusCode >= 500
    ? "failed"
    : hasUsage ? "completed" : "usage_missing";

var usageEvent = new GatewayUsageEvent(
    requestId,
    authResult.UserId!,
    authResult.ApiKeyId!,
    authResult.ApiKeyPrefix!,
    metadata.Model,
    lease.NodeId,
    usage,
    status,
    startedAt,
    completedAt);
_ = billingClient.RecordUsageAsync(usageEvent, CancellationToken.None);
```

В streaming `ExecuteStreamingAsync` установить:

```csharp
var status = usage.TotalTokens > 0 ? "completed" : "usage_missing";
```

и передать `authResult.ApiKeyPrefix!` в `GatewayUsageEvent`.

- [ ] **Шаг 6: Запустить gateway tests**

Команда:

```powershell
dotnet test tests/OpenRussiaRouter.ApiGateway.Tests -nr:false -m:1
```

Ожидаемо: PASS.

- [ ] **Шаг 7: Commit ApiGateway changes**

```powershell
git add src/OpenRussiaRouter.ApiGateway tests/OpenRussiaRouter.ApiGateway.Tests
git commit -m "feat: record gateway activity by api token"
```

---

### Задача 6: Показать ActivityByToken в WebApp

**Файлы:**
- Создать: `src/OpenRussiaRouter.WebApp/Services/Billing/ActivityByTokenInfo.cs`
- Изменить: `src/OpenRussiaRouter.WebApp/Services/Billing/BillingClient.cs`
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml.cs`
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml`

- [ ] **Шаг 1: Добавить WebApp DTO**

Создать:

```csharp
namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ActivityByTokenInfo(
    string RequestId,
    string ApiKeyId,
    string ApiKeyPrefix,
    string ModelId,
    string RouterNodeId,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt);
```

- [ ] **Шаг 2: Добавить BillingClient method**

Добавить:

```csharp
public async Task<IReadOnlyList<ActivityByTokenInfo>> ListActivityByTokenAsync(
    IReadOnlyCollection<string> apiKeyIds,
    CancellationToken cancellationToken = default)
{
    if (apiKeyIds.Count == 0)
    {
        return [];
    }

    var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListActivityByTokenRequest();
    request.ApiKeyIds.AddRange(apiKeyIds);
    var response = await client.ListActivityByTokenAsync(request, cancellationToken: cancellationToken);

    return response.Records.Select(r => new ActivityByTokenInfo(
        r.RequestId,
        r.ApiKeyId,
        r.ApiKeyPrefix,
        r.ModelId,
        r.RouterNodeId,
        r.Usage?.InputTokens ?? 0,
        r.Usage?.OutputTokens ?? 0,
        r.Usage?.CacheTokens ?? 0,
        r.Status,
        DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(r.CompletedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
}
```

- [ ] **Шаг 3: Загружать keys и token activity в page model**

Обновить constructor в `Activity.cshtml.cs`, чтобы он получал `AuthClient`:

```csharp
private readonly BillingClient _billingClient;
private readonly AuthClient _authClient;

public IReadOnlyList<ActivityByTokenInfo> Activity { get; set; } = [];

public ActivityModel(BillingClient billingClient, AuthClient authClient)
{
    _billingClient = billingClient;
    _authClient = authClient;
}
```

Обновить `OnGetAsync`:

```csharp
public async Task OnGetAsync()
{
    try
    {
        var keys = await _authClient.ListApiKeysAsync(User.GetUserId());
        Activity = await _billingClient.ListActivityByTokenAsync(keys.Select(key => key.Id).ToArray());
    }
    catch
    {
        ErrorMessage = "Failed to load activity data.";
    }
}
```

- [ ] **Шаг 4: Отрендерить token activity**

Обновить table headers:

```html
<th>Date</th><th>Token</th><th>Model</th><th>Input</th><th>Output</th><th>Cache</th><th>Status</th>
```

Использовать `Model.Activity` и render prefix:

```cshtml
@foreach (var r in Model.Activity)
{
    <tr>
        <td>@r.CreatedAt.ToLocalTime().ToString("g")</td>
        <td><code>@r.ApiKeyPrefix***</code></td>
        <td><code>@r.ModelId</code></td>
        <td class="num">@r.InputTokens.ToString("N0")</td>
        <td class="num">@r.OutputTokens.ToString("N0")</td>
        <td class="num">@r.CacheTokens.ToString("N0")</td>
        <td>
            <span class="badge @(r.Status == "completed" ? "badge-success" : "badge-warning")">@r.Status</span>
        </td>
    </tr>
}
```

Обновить empty state, чтобы он проверял `Model.Activity.Count`.

- [ ] **Шаг 5: Собрать WebApp**

Команда:

```powershell
dotnet build src/OpenRussiaRouter.WebApp -nr:false -m:1
```

Ожидаемо: PASS.

- [ ] **Шаг 6: Commit WebApp changes**

```powershell
git add src/OpenRussiaRouter.WebApp
git commit -m "feat: show activity by api token"
```

---

### Задача 7: Полная проверка и regression tests

**Файлы:**
- Изменить любой test file, затронутый предыдущими задачами, если build покажет fallout из-за новых constructor signatures.

- [ ] **Шаг 1: Restore dependencies**

Команда:

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
```

Ожидаемо: restore succeeds.

- [ ] **Шаг 2: Build solution**

Команда:

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Ожидаемо: build succeeds.

- [ ] **Шаг 3: Run unit and integration tests**

Команда:

```powershell
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

Ожидаемо: all tests pass. PostgreSQL integration tests остаются opt-in, если не выставлены environment variables: `ORR_AUTH_POSTGRES_TEST_CONNECTION`, `ORR_BILLING_POSTGRES_TEST_CONNECTION`, `ORR_CONTROL_POSTGRES_TEST_CONNECTION`.

- [ ] **Шаг 4: Проверить schema на запрещенные columns**

Команда:

```powershell
rg -n "activity_by_token|user_id|cost_amount|cost_currency|raw_key|secret_hash" db\\migrations\\billing src\\OpenRussiaRouter.BillingService
```

Ожидаемо: migration и entity для `activity_by_token` не содержат `user_id`, `cost_amount`, `cost_currency`, `raw_key` или `secret_hash`.

- [ ] **Шаг 5: Commit verification fixes**

Если Step 2 или Step 3 потребовали правок, закоммитить их:

```powershell
git add .
git commit -m "test: cover activity by token flow"
```

Если правки не понадобились, этот commit не нужен.

---

## Self-review notes

- Покрытие spec: Auth prefix, таблица BillingService, отсутствие `user_id`, отсутствие cost fields, отсутствие full token storage, usage normalization, Activity UI и тесты покрыты задачами 1-7.
- Placeholder scan: план содержит конкретные файлы, команды, code snippets и expected outcomes.
- Type consistency: `ApiKeyPrefix` используется в Auth contract, Gateway auth result, Gateway usage event, Billing `UsageEvent`, Billing gRPC request, Billing domain activity record, WebApp DTO и UI rendering.
