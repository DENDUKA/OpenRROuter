# ControlPlane — CLAUDE.md

Реестр нод 9Router, health-чеки, лизинг (least-load), capability-aware routing, синхронизация
пользовательских `sk-or-*` ключей на ноды, прокси к admin API нод. **gRPC-сервер** (потребители
ждут его на `:5003`) + HTTP `/health`. Layered: Domain → Application → Infrastructure.
Своя БД — схема `control` в PostgreSQL.

## Bootstrap ([Program.cs](Program.cs), [Startup.cs](Infrastructure/Startup/Startup.cs))

Seq-логирование + OTel-метрики → `Startup.ConfigureServices` → `ApplySqlMigrationsAsync("ControlPlaneDb",
"control", RepositoryMigrationPath.ForService("control"))` **до** конфигурации pipeline →
`MapGrpcService<GrpcRouterControlPlaneService>` + контроллеры + `/health`.

- Все опции — `ValidateDataAnnotations().ValidateOnStart()`: секции `RouterHealthChecks`,
  `RouterNodeProviderCache`, `RouterLeaseCleanup`, `RouterNodeSnapshot`, `CapabilityReplica`. Невалидный конфиг роняет старт.
- gRPC-клиенты: **Auth** (`GrpcServices:Auth`, дефолт `http://localhost:5001`) и **Collector**
  (`GrpcServices:Collector`, `:5005`) — keep-alive + `GrpcDeadlineInterceptor`.
- **`ConnectionStrings:ControlPlaneDb`** (именно так, не `ControlDb`). Пустая строка ⇒
  DI подставляет `InMemoryControlPlaneRepository` и сервис **молча стартует без БД**, теряя реестр
  при рестарте. Если «ноды исчезли» — первым делом проверь connection string.
- Свой gRPC-сервер **не закрыт s2s-интерсептором** (в отличие от Billing/Content):
  `SelectRouterNode`, `ListRouterNodesForInternalConsumers`, `GetRouterNodeDetails` отдают
  `control_token` любому, кто дотянулся до порта. Защита чисто сетевая (NetworkPolicy /
  compose-сеть) — порт наружу не публиковать.
- Порты: контейнер — HTTP `8080` (`ASPNETCORE_URLS` в [Dockerfile](Dockerfile)), в k8s дополнительно
  gRPC `8081` через `Kestrel__Endpoints__*`. Локальный `dotnet run` поднимается на **5016**
  (launchSettings), поэтому потребителям с дефолтом `:5003` нужен либо `ASPNETCORE_URLS`,
  либо их `GrpcServices:ControlPlane`.
- HTTP-поверхность: `/health` (health checks из `Startup`), плюс `/healthz`, `/ready`, `/`
  ([HealthController.cs](Infrastructure/Http/HealthController.cs)).

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/` | `RouterNode` (логика статуса/лизинга/валидации внутри: `Lease()`, `Release()`, `ApplyHealth()`, `Update()`, `UpdateControlToken()`, `Disable()`/`Enable()`), `RouterLease` (record), `RouterNodeStatus` |
| Application | `Application/Services` | `RouterControlPlaneService` (фасад), `NodeModelCapabilityStore`, `RouterNodeSnapshotStore` |
| Application | `Application/Abstractions` | `IControlPlaneRepository`, `IRouterNodeProvider`, `IRouterNodeHealthProvider` |
| Application | `Application/Contracts` | DTO admin API нод + `RouterNodeProviderException` |
| Application | `Application/Options` | `RouterHealthCheckOptions`, `RouterLeaseCleanupOptions`, `RouterNodeSnapshotOptions`, `RouterNodeProviderCacheOptions`, `CapabilityReplicaOptions` |
| Application | `Application/Extensions` | `AddControlPlaneInfrastructure()` / `AddControlPlaneApplicationServices()` |
| Infrastructure | `Infrastructure/Grpc` | `GrpcRouterControlPlaneService` — gRPC-фасад + оркестрация key-sync |
| Infrastructure | `Infrastructure/Http` | `RouterNodeProvider` (admin API 9Router), `CachedRouterNodeProvider` + `RouterNodeProviderCacheStore`, `RouterNodeHealthProvider`, `HealthController` |
| Infrastructure | `Infrastructure/Workers` | `RouterHealthCheckWorker`, `RouterLeaseCleanupWorker`, `RouterNodeSnapshotRefresher`, `CapabilityReplicaRefresher` (**воркеры живут здесь, не в Application/Services**) |
| Infrastructure | `Infrastructure/Persistence` | Dapper-entity, `ControlPlanePersistenceMapper`, connection factory |
| Infrastructure | `Infrastructure/Repository` | `ControlPlaneRepository` (PG) + `InMemoryControlPlaneRepository` |

## Выбор ноды — `RouterControlPlaneService.SelectRouterNodeAsync` ([файл](Application/Services/RouterControlPlaneService.cs))

Алгоритм (hot-path):
1. кандидаты = последний полный **in-memory снимок метаданных** (`RouterNodeSnapshotRefresher`, 10 с),
   отфильтрованный `Enabled && Status == Healthy`; если снимок ещё пуст после старта, один раз
   читается репозиторий;
2. **capability-фильтр**: если `modelId` задан **и** `!capabilityStore.IsEmpty` → оставить только
   `capabilityStore.NodeSupportsModel(nodeId, modelId)`. Пустой store (сразу после старта, до первого
   `CapabilityReplicaRefresher`) — **fallback на least-load без фильтра**, первые запросы не отклоняются;
3. вычесть `excludedNodeIds` (retry/failover от ApiGateway, сравнение case-insensitive);
4. при `preferredNodeId` — оставить только её, **сверив и `ControlToken`** (ordinal);
5. передать только ID кандидатов в `TryAcquireLeaseAsync`. PostgreSQL в одной транзакции повторно
   проверяет `enabled/status`, берёт строку `FOR UPDATE SKIP LOCKED`, выбирает least-load по живому
   `in_flight_requests / capacity_weight`, инкрементирует счётчик и вставляет лизу.

Снимок никогда не хранит `in_flight_requests`: нагрузка — исключительно состояние БД. Обычное
освобождение лизы и cleanup протухших лиз выполняются CTE-операциями с `greatest(..., 0)`;
cleanup группирует удалённые лизы по ноде и уменьшает счётчик ровно на их число. Health обновляет
только health-поля и не может затереть нагрузку; отключённая нода остаётся `Disabled`.

Пустой список кандидатов → `InvalidOperationException`, gRPC-фасад превращает его в
`ErrorCode = ErrorCodes.RouterUnavailable` (не в gRPC-ошибку).
`ReleaseRouterNodeAsync(leaseId)` — снять лиз и `node.Release()` (--inflight).

## Capability-aware routing

- **`modelId` здесь — это `RouterModelId`** (`glm-4.7`), а не публичный `ModelId` (`ORR/GLM-4.7`):
  store наполняется тем, что ноды сами репортят. ApiGateway передаёт `balanceResult.RouterModelId`
  (`GatewayPipeline.AuthorizeAndAcquireLeaseAsync`); передать публичный id = ни одна нода не сматчится.
- `NodeModelCapabilityStore` ([файл](Application/Services/NodeModelCapabilityStore.cs)) —
  `Dictionary<nodeId, IReadOnlySet<modelId>>` **под `lock`** (не lock-free и не `ConcurrentDictionary`).
  `IsEmpty`, `NodeSupportsModel`, `SetNodeModels`, `ReplaceAll` (атомарная замена снапшота).
  Ключи нод — `Ordinal`, множества моделей — `OrdinalIgnoreCase`.
- `CapabilityReplicaRefresher` ([файл](Infrastructure/Workers/CapabilityReplicaRefresher.cs)) —
  `BackgroundService`: каждые `CapabilityReplica:RefreshInterval` тянет `GetCapabilitiesSnapshot`
  из **CollectorService** по gRPC и делает `store.ReplaceAll`. **При сбое коллектора store не трогается** —
  роутинг работает на устаревших подсказках (log warning). Заменил прежний in-process Quartz-джоб
  (в комментариях кода Quartz ещё упоминается — это исторический след).
- Зависимость с Collector **круговая**: ControlPlane тянет из коллектора capabilities, коллектор
  тянет из ControlPlane список нод (`ListRouterNodesForInternalConsumers`). После одновременного
  рестарта обоих первые запросы идут по least-load-фолбэку.

## Health-чеки

`RouterHealthCheckWorker` ([файл](Infrastructure/Workers/RouterHealthCheckWorker.cs)) — каждые
`RouterHealthChecks:Interval` последовательно обходит enabled-ноды: `healthClient.CheckAsync` →
`controlPlane.ApplyHealthAsync(...)`. **Нет cooldown/anti-flapping** — статус меняется мгновенно
(CONTROL-002). Отдельный `RouterLeaseCleanupWorker` чистит протухшие лизы; `RouterNodeSnapshotRefresher`
раз в `RouterNodeSnapshot:RefreshInterval` заменяет снимок метаданных только после полного успешного чтения.

`RouterNodeHealthProvider` ([файл](Infrastructure/Http/RouterNodeHealthProvider.cs)):
GET `{EndpointUrl}{HealthPath}`, **только заголовок `NodeTokenHeader` (`X-ORR-Node-Token`) с control-токеном**
— здесь `Authorization: Bearer` не отправляется (в отличие от `RouterNodeProvider`). Не-2xx →
`Unhealthy` + `http_{code}`; таймаут → `timeout`; сетевая ошибка → `network_error`. При 2xx тело
парсится опционально: `status` ∈ `healthy|degraded|unhealthy`, иначе `Healthy`.

## Admin API нод — `RouterNodeProvider` ([файл](Infrastructure/Http/RouterNodeProvider.cs))

HTTP-клиент (timeout 10с) к `/api/integrations/*` каждой ноды. **Аутентификация:
`Authorization: Bearer <node.ControlToken>` и дублирование в `X-ORR-Node-Token`** — токен берётся
прямо из сущности, никакого резолва через конфиг. Пустой `ControlToken` → `InvalidOperationException`
ещё до запроса. Методы: provider quota (`GET .../providers/quota[?includeInactive=true]`),
provider status (`PATCH .../providers/{id}/status`), integration keys (`GET/POST .../keys`,
`PATCH .../keys/{id}/status`, `DELETE .../keys/{id}`), bulk `POST .../keys/sync`.
Ошибки → `RouterNodeProviderException` (статус, распарсенный `error` из тела, метод/URI/тело).

`CachedRouterNodeProvider` ([файл](Infrastructure/Http/CachedRouterNodeProvider.cs)) — обёртка,
зарегистрированная как `IRouterNodeProvider`: кеширует **только чтения** (quota, list keys) на
`RouterNodeProviderCache:Ttl`, любая запись инвалидирует все ключи ноды. Следствие: админка может
до 30с видеть устаревшие данные ноды, изменённой в обход ControlPlane.

## gRPC-контракт ([control-plane.proto](../OpenRussiaRouter.Contracts/Protos/control-plane.proto))

| Группа | RPC |
|---|---|
| Лизинг | `SelectRouterNode`, `ReleaseRouterNode` |
| CRUD нод | `RegisterRouterNode`, `UpdateRouterNode`, `UpdateRouterNodeControlToken`, `EnableRouterNode`, `DisableRouterNode`, `DeleteRouterNode` |
| Чтение | `ListRouterNodes` (**без** `control_token`, поле зарезервировано), `ListRouterNodesForInternalConsumers` (`RouterNodeAdminSummary` **с** `control_token`, потребитель — Collector), `GetRouterNodeDetails` |
| Ключи | `PushUserIntegrationKey`, `UpdateUserIntegrationKeyStatus`, `DeleteUserIntegrationKey`, `SyncNodeIntegrationKeys`, `CheckNodeIntegrationKeysSync` |

Ошибки отдаются полем `error_code` (`ErrorCodes.ValidationFailed` / `RouterUnavailable`), не статусами gRPC.
В proto остался осиротевший `RevokeUserIntegrationKeyResponse` — RPC `RevokeUserIntegrationKey` больше нет,
его роль играет `UpdateUserIntegrationKeyStatus(is_active)`.

## Синхронизация ключей — `GrpcRouterControlPlaneService` ([файл](Infrastructure/Grpc/GrpcRouterControlPlaneService.cs))

- **`PushUserIntegrationKey`**: пушит ключ на все enabled-ноды (или одну `NodeId`) через
  `CreateIntegrationKeyAsync`; маппинг `(apiKeyId, nodeId, routerKeyId)` → `control.integration_key_sync`
  (`SaveIntegrationKeyMappingAsync`). Ошибки по нодам — log warning, весь пуш не валят,
  в ответе только `pushed_count`.
- **`UpdateUserIntegrationKeyStatus`**: по маппингам → `UpdateIntegrationKeyStatusAsync(is_active)`.
  **Маппинг НЕ удаляет** — связь нужна для последующего полного удаления.
- **`DeleteUserIntegrationKey`**: `DeleteIntegrationKeyAsync` по каждому маппингу, затем
  `RemoveIntegrationKeyMappingsAsync` (чистим только после попытки физического удаления).
- **`SyncNodeIntegrationKeys`**: тянет из **AuthService** `ListAllNonDeletedApiKeysRawAsync`
  (именно non-deleted, а не только активные — неактивные уезжают с `IsActive=false`) и bulk-пушит
  на ноду; возвращает added/disabled/reactivated/deleted.
- **`CheckNodeIntegrationKeysSync`**: read-only предпросмотр расхождений для админки
  (`/admin/nodes/{id}`), ничего не пишет. Сравнивает ключи БД с ключами ноды, **сопоставляя
  `dbKey.RawKey` с `nodeKey.Id`**; на удаление считает только ключи ноды с `managed_by == "service"`.
- `GetRouterNodeDetails`: агрегирует quota + integration keys, каждый вызов под таймаутом 3с
  (`NodeDetailsProviderTimeout`), ошибки складывает в `api_error`, ответ не валит.

## БД (схема `control`) и миграции

| Таблица | Что |
|---|---|
| `control.router_nodes` | id, name, endpoint_url, **control_token**, capacity_weight, enabled, status, in_flight_requests, last_checked_at_utc, last_health_latency_ms, last_health_error_code, created_at_utc |
| `control.router_leases` | id, node_id → router_nodes(id), request_id, expires_at_utc, created_at_utc |
| `control.integration_key_sync` | PK `(api_key_id, node_id)`, router_key_id, pushed_at_utc; FK на ноду с `on delete cascade` |

Миграции — `db/migrations/control/` (**6 файлов**): `001_initial`, `002_rename_token_secret_name`
(`token_secret_name` → `control_token`), `003_add_missing_indexes` (индекс по `name`),
`004_integration_key_sync`, `005_fix_node_indexes` (составной `(enabled, status)` вместо двух отдельных),
`006_add_cascade_on_leases_fk` (`ON DELETE CASCADE` для `router_leases.node_id`).

Статус и in-flight хранятся в строке ноды: `ControlPlanePersistenceMapper.ToDomain` восстанавливает
`InFlightRequests` **реплеем `Lease()`** и статус через `ApplyHealth`/`Disable`.

## Конфигурация

| Ключ | Дефолт в коде | Заметки |
|---|---|---|
| `RouterHealthChecks:Enabled` / `Interval` / `Timeout` | `true` / 30с / 5с | prod-оверрайд: 1мин / 10с |
| `RouterHealthChecks:HealthPath` | `/healthz` | **compose (dev+prod) и dev-оверлей k8s ставят `/api/health`**; базовый k8s-манифест ставит `/v1/models`, по которому f9router отдаёт 401 и нода никогда не станет Healthy |
| `RouterHealthChecks:NodeTokenHeader` | `X-ORR-Node-Token` | единственный заголовок health-запроса |
| `RouterNodeProviderCache:Enabled` / `Ttl` | `true` / 30с | кеш чтений admin API нод |
| `RouterLeaseCleanup:Enabled` / `Interval` | `true` / 30с | prod: 1мин |
| `RouterNodeSnapshot:Enabled` / `RefreshInterval` | `true` / 10с | hot-path читает только этот снимок; пустой снимок имеет стартовый DB fallback |
| `CapabilityReplica:Enabled` / `RefreshInterval` | `true` / 1мин | опрос коллектора |
| `GrpcServices:Auth` / `:Collector` / `:CallDeadlineSeconds` | `:5001` / `:5005` / 30 | `Collector` есть в `appsettings.json`, `Auth` — только дефолт в коде |
| `ConnectionStrings:ControlPlaneDb` | пусто | пусто ⇒ in-memory репозиторий |

Ключа `RouterHealthChecks:NodeTokens` в коде нет — в compose-файлах остался мёртвый
`RouterHealthChecks__NodeTokens: ""`, он ни на что не влияет.

## Частые заблуждения (НЕ повторять)

| Неверно | Верно |
|---|---|
| `control_token` в БД = имя секрета | `control_token` = **реальное значение токена**, шлётся напрямую `Bearer` (историческое имя колонки `token_secret_name` переименовано миграцией 002) |
| `RouterHealthChecks:NodeTokens` резолвит токен | Такой логики нет — провайдеры берут `node.ControlToken` |
| Валидация `control_token`/endpoint где-то в инфраструктуре | Уже в доменной сущности `RouterNode` (endpoint — только http/https, http разрешён для ClusterIP) — **не флагать как пропуск** |
| Воркеры лежат в `Application/Services` | Все четыре — в `Infrastructure/Workers` |
| Capability-store можно спрашивать публичным `ModelId` | Только `RouterModelId` |
| В капабилити-сторе `ConcurrentDictionary` / lock-free чтение | Обычный `Dictionary` под `lock` |

## Подводные камни

- **Не возвращай read-modify-write для нагрузки.** Лизинг, release и expiry cleanup обязаны идти
  через атомарные методы репозитория; `UpsertNodeAsync` намеренно не перезаписывает
  `in_flight_requests`, а health пишется точечным `ApplyHealthAsync`.
- **Снимок метаданных может устареть.** Это допустимо только как предварительный фильтр: SQL при
  выдаче лизы повторно проверяет `enabled` и `Healthy`, поэтому stale snapshot не выдаст лизу
  отключённой или нездоровой ноде.
- **`DeleteRouterNode` с живыми лизами.** У `router_leases.node_id` FK был без `on delete
  cascade` (миграция 001). Миграция 006 добавила `ON DELETE CASCADE`, а `RemoveNodeAsync`
  дополнительно удаляет лизы явно перед удалением ноды (как страховка и для in-memory
  репозитория). gRPC-фасад ловит `KeyNotFoundException` (нода не найдена) и отдаёт
  `ValidationFailed`.
- **Отключённая нода** (`Disable()`) принудительно получает `Status = Disabled`, и `ApplyHealth`
  для неё всегда возвращает `Disabled` — health-чек её не «оживит», нужен `EnableRouterNode`.

## Тесты

`tests/OpenRussiaRouter.ControlPlane.Tests` — unit на `InMemoryControlPlaneRepository` + фейковые
провайдеры (`RouterControlPlaneServiceTests`, `GrpcRouterControlPlaneServiceTests`,
`CapabilityReplicaRefresherTests`, `RouterHealthCheckWorkerTests`, `RouterLeaseCleanupWorkerTests`,
`RouterNodeSnapshotRefresherTests`,
`CachedRouterNodeProviderTests`, `RouterNodeProviderClientTests`, `HttpRouterNodeHealthClientTests`) и
Postgres-интеграционные на Testcontainers (`ControlPlaneRepositoryPostgresTests`,
`GrpcRouterControlPlaneServicePostgresIntegrationTests`, `PostgresTestDatabase`). Переопределение
строки подключения — `ORR_CONTROL_POSTGRES_TEST_CONNECTION` (тесты не включает, только переопределяет).
