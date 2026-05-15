# API Key Sync — TODO: синхронизация ключей с 9Router нодами

## Контекст

У каждой 9Router ноды есть **локальное хранилище API-ключей** (ROUTER-006).
Когда пользователь создаёт или отзывает ключ в OpenRussiaRouter — ноды должны узнать об этом,
чтобы при обращении через Gateway пользователь был пропущен/заблокирован без лишних roundtrip'ов
в Auth Service.

Дополнительно: Gateway должен **пробрасывать оригинальный Bearer-токен** на 9Router,
чтобы нода могла его проверить локально.

---

## Схема потока (итог)

```
1. Пользователь создаёт ключ → Auth Service
2. Auth Service → Control Plane (gRPC): "ключи изменились"  [KEYSYNC-003]
3. Control Plane → все активные 9Router ноды (HTTP): POST /internal/keys/sync  [KEYSYNC-002]
4. Ноды обновляют локальное хранилище

При запросе:
5. Клиент → Gateway: Authorization: Bearer sk-abc1_secret
6. Gateway → Auth Service: ValidateApiKey (проверка + billing)
7. Gateway → 9Router: запрос + Authorization: Bearer sk-abc1_secret  [KEYSYNC-005]
8. 9Router: sha256(secret) сверяет с хранилищем → пропускает  [ROUTER-006]
```

---

## KEYSYNC-001 — Новый gRPC метод в Auth Service: `ListActiveApiKeyHashes`

**Сервис:** `OpenRussiaRouter.AuthService`

**Зачем:** Control Plane вызывает этот метод, чтобы получить актуальный список всех
активных (не отозванных) ключей для синхронизации с нодами.

**Добавить в `auth.proto`:**

```protobuf
rpc ListActiveApiKeyHashes (ListActiveApiKeyHashesRequest)
    returns (ListActiveApiKeyHashesResponse);

message ListActiveApiKeyHashesRequest {}

message ApiKeyHashEntry {
  string prefix      = 1;  // публичный prefix ключа
  string secret_hash = 2;  // sha256 от секретной части, формат: "sha256:{hex}"
}

message ListActiveApiKeyHashesResponse {
  repeated ApiKeyHashEntry keys = 1;
}
```

**Реализация** (Auth Service):
- Запрос к `auth` БД: `SELECT prefix, secret_hash FROM api_keys WHERE revoked_at IS NULL`
- Вернуть список `{prefix, secret_hash}`
- Не фильтровать по user — нужны все активные ключи всех пользователей

**Безопасность:**
- Метод доступен только внутренним сервисам (нет публичного gateway, только gRPC)
- `secret_hash` — это SHA-256 хеш, не raw secret; безопасно передавать между сервисами

**Файлы:**
- `src/OpenRussiaRouter.Contracts/Protos/auth.proto`
- `src/OpenRussiaRouter.AuthService/Application/GrpcAuthService.cs`
- `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthRepository.cs`

**Тесты:**
- Метод возвращает только не отозванные ключи
- Возвращает ключи всех пользователей
- Отозванный ключ не попадает в список

---

## KEYSYNC-002 — Сервис синхронизации ключей в Control Plane

**Сервис:** `OpenRussiaRouter.ControlPlane`

**Зачем:** Control Plane знает все активные 9Router ноды и умеет достучаться до каждой.
Нужен компонент, который вызывает `POST /internal/keys/sync` на каждой enabled ноде.

**Что реализовать:**

### Интерфейс

```csharp
// src/OpenRussiaRouter.ControlPlane/Application/IRouterKeySyncService.cs
public interface IRouterKeySyncService
{
    // Полная синхронизация всех ключей на все enabled ноды
    Task SyncAllAsync(CancellationToken ct);

    // Синхронизация на конкретную ноду (при регистрации)
    Task SyncToNodeAsync(string nodeId, CancellationToken ct);

    // Немедленная блокировка ключа на всех нодах
    Task RevokeKeyAsync(string prefix, CancellationToken ct);
}
```

### Реализация `RouterKeySyncService`

1. Вызвать `AuthService.ListActiveApiKeyHashes()` (gRPC) → получить список хешей
2. Для каждой enabled ноды вызвать `POST /internal/keys/sync` (HTTP, auth: NODE_TOKEN)
3. При ошибке на ноде — логировать, не прерывать синхронизацию остальных нод
4. `RevokeKeyAsync` → вызвать `DELETE /internal/keys/{prefix}` на всех нодах

### HTTP клиент для 9Router

```csharp
// src/OpenRussiaRouter.ControlPlane/Infrastructure/RouterNodeHttpClient.cs
public class RouterNodeHttpClient
{
    Task SyncKeysAsync(string nodeEndpoint, string nodeToken, IEnumerable<ApiKeyHashEntry> keys, CancellationToken ct);
    Task RevokeKeyAsync(string nodeEndpoint, string nodeToken, string prefix, CancellationToken ct);
}
```

**Зависимости, которые нужно добавить в Control Plane:**
- gRPC клиент к Auth Service (`AuthGrpc.AuthServiceClient`)
- `IAuthRepository` или gRPC вызов `ListActiveApiKeyHashes`
- Конфиг: `GrpcServices:Auth` уже есть в `appsettings.json`

**Файлы:**
- `src/OpenRussiaRouter.ControlPlane/Application/IRouterKeySyncService.cs` (новый)
- `src/OpenRussiaRouter.ControlPlane/Infrastructure/RouterKeySyncService.cs` (новый)
- `src/OpenRussiaRouter.ControlPlane/Infrastructure/RouterNodeHttpClient.cs` (новый)
- `src/OpenRussiaRouter.ControlPlane/Infrastructure/Startup/ControlPlaneExtensions.cs` (обновить)

**Тесты:**
- `SyncAllAsync`: все enabled ноды получают запрос с актуальным списком
- `SyncToNodeAsync`: только указанная нода получает запрос
- Ошибка на одной ноде не прерывает синхронизацию остальных
- `RevokeKeyAsync`: все ноды получают DELETE запрос

---

## KEYSYNC-003 — Триггер синхронизации при регистрации ноды

**Сервис:** `OpenRussiaRouter.ControlPlane`

**Зачем:** когда добавляется новая нода, она пустая. Нужно сразу залить все существующие ключи.

**Что изменить:**

В `GrpcRouterControlPlaneService.RegisterRouterNode`:
```csharp
// После успешной регистрации:
_ = keySyncService.SyncToNodeAsync(newNode.Id, CancellationToken.None);
```

Это fire-and-forget — регистрация не ждёт синхронизации, она происходит в фоне.

**Файлы:**
- `src/OpenRussiaRouter.ControlPlane/Grpc/GrpcRouterControlPlaneService.cs`

**Тесты:**
- После регистрации ноды она получает `POST /internal/keys/sync` с актуальными ключами
- Ошибка синхронизации не блокирует ответ на `RegisterRouterNode`

---

## KEYSYNC-004 — Триггер синхронизации при создании/отзыве ключа

**Зачем:** немедленно распространить изменение на все ноды без ожидания периодической синхронизации.

**Подход (MVP — периодическая синхронизация):**

Добавить фоновый воркер в Control Plane, который каждые **5 минут** вызывает `SyncAllAsync`.
Это проще, чем добавлять зависимость Auth Service → Control Plane.

```csharp
// src/OpenRussiaRouter.ControlPlane/Infrastructure/Background/KeySyncWorker.cs
public class KeySyncWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(Interval, ct);
            await keySyncService.SyncAllAsync(ct);
        }
    }
}
```

**Подход (расширенный — немедленный триггер):**

Если нужна немедленная синхронизация при создании/отзыве ключа:
1. Добавить gRPC метод `TriggerKeySync` в Control Plane proto
2. Auth Service вызывает его после `CreateApiKey` / `RevokeApiKey`
3. Control Plane запускает `SyncAllAsync` в фоне

> **Рекомендация:** начать с периодической синхронизации (5 мин).
> Добавить немедленный триггер после того, как периодика заработает.

**Файлы:**
- `src/OpenRussiaRouter.ControlPlane/Infrastructure/Background/KeySyncWorker.cs` (новый)
- `src/OpenRussiaRouter.ControlPlane/Infrastructure/Startup/ControlPlaneExtensions.cs` (добавить `services.AddHostedService<KeySyncWorker>()`)

**Тесты:**
- Воркер вызывает `SyncAllAsync` каждые N минут
- Воркер останавливается при cancellation
- Ошибка синхронизации логируется, воркер продолжает работу

---

## KEYSYNC-005 — Gateway: пробрасывать оригинальный Bearer-токен на 9Router

**Сервис:** `OpenRussiaRouter.ApiGateway`

**Зачем:** 9Router должен получить `Authorization: Bearer {raw_key}`, чтобы проверить его
против своего локального хранилища.

**Что изменить:**

### `IRouterProxyClient` — добавить параметр `userApiKey`

```csharp
Task<RouterProxyResponse> SendChatCompletionAsync(
    GatewayRouterLease lease,
    string requestJson,
    string requestId,
    string userApiKey,       // <-- новый параметр
    CancellationToken cancellationToken);

Task<RouterStreamSession> BeginStreamChatCompletionAsync(
    GatewayRouterLease lease,
    string requestJson,
    string requestId,
    string userApiKey,       // <-- новый параметр
    CancellationToken cancellationToken);
```

### `YarpRouterProxyClient` — добавить заголовок Authorization

В `BeginStreamChatCompletionAsync`:
```csharp
request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + userApiKey);
```

В `SendChatCompletionAsync` (YARP ForwardingContext):
```csharp
context.Request.Headers["Authorization"] = "Bearer " + userApiKey;
```

### `Startup.cs` — передать `apiKey` в методах

В `HandleChatCompletionsAsync` и `ExecuteStreamingAsync` `apiKey` уже доступен
после `BearerTokenReader.TryRead(...)`. Передать его в вызов `routerProxy`.

**Файлы:**
- `src/OpenRussiaRouter.ApiGateway/Router/IRouterProxyClient.cs`
- `src/OpenRussiaRouter.ApiGateway/Router/YarpRouterProxyClient.cs`
- `src/OpenRussiaRouter.ApiGateway/Infrastructure/Startup/Startup.cs`

**Важно:** raw API key **не логируется** (уже соблюдается в текущем коде — заголовок
Authorization не пишется в логи через Serilog/HttpLogging конфиг).

**Тесты:**
- Запрос к 9Router содержит заголовок `Authorization: Bearer {raw_key}`
- Raw key не появляется в application logs

---

## Порядок реализации

```
KEYSYNC-001  →  KEYSYNC-002  →  KEYSYNC-003
                                    ↓
                             KEYSYNC-004 (периодика)
                                    ↓
                             KEYSYNC-005 (Gateway)
                                    ↓
                              ROUTER-006 (9Router)
```

Можно параллельно: KEYSYNC-001..004 (сторона .NET) и ROUTER-006 (сторона 9Router),
затем интеграционный тест конца-в-конец.

---

## Итоговые изменения по сервисам

| Сервис | Что меняется |
|---|---|
| **Contracts** (`auth.proto`) | + `ListActiveApiKeyHashes` |
| **AuthService** | + реализация `ListActiveApiKeyHashes` в gRPC и репозитории |
| **ControlPlane** | + `IRouterKeySyncService`, `RouterNodeHttpClient`, `KeySyncWorker` |
| **ApiGateway** | + передача `userApiKey` в proxy клиент, добавление `Authorization` заголовка |
| **9Router** | + `POST /internal/keys/sync`, `DELETE /internal/keys/{prefix}`, Bearer middleware |
