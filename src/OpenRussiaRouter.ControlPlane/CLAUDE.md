# ControlPlane — CLAUDE.md

Реестр нод 9Router, health-чеки, лизинг (least-load), capability-aware routing, синхронизация пользовательских `sk-or-*` ключей на ноды, прокси к admin API нод. **gRPC-сервер (порт 5003)** + HTTP `/health`. Layered: Domain → Application → Infrastructure.

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities` | `RouterNode` (вся логика статуса/лизинга/валидации `control_token` внутри: `Lease()`, `Release()`, `ApplyHealth()`, `Update()`, `Disable()`/`Enable()`), `RouterLease` |
| Application | `Application/Services` | `RouterControlPlaneService` (фасад выбора ноды), `NodeModelCapabilityStore`, воркеры, провайдеры-интерфейсы, options |
| Infrastructure | `Infrastructure/Grpc` | `GrpcRouterControlPlaneService` — gRPC-фасад + оркестрация key-sync |
| Infrastructure | `Infrastructure/Http` | `RouterNodeProvider` (admin API 9Router), `RouterNodeHealthProvider`, `CachedRouterNodeProvider` + cache-store |
| Infrastructure | `Infrastructure/Persistence` | Dapper-entity, `ControlPlanePersistenceMapper`, connection factory |
| Infrastructure | `Infrastructure/Repository` | `ControlPlaneRepository` (PG) + `InMemoryControlPlaneRepository` |

## Выбор ноды — `RouterControlPlaneService.SelectRouterNodeAsync` ([файл](Application/Services/RouterControlPlaneService.cs))

Алгоритм (hot-path):
1. `ReleaseExpiredLeasesAsync(nowUtc)` — снять протухшие лизы (TTL `DefaultLeaseTtl` = 5 мин);
2. кандидаты = ноды с `Enabled && Status==Healthy`;
3. **capability-фильтр**: если `modelId` задан **и** `!capabilityStore.IsEmpty` → оставить только `capabilityStore.NodeSupportsModel(nodeId, modelId)`. Если store пуст (сразу после старта, до первого `CapabilityReplicaRefresher`) — **fallback на least-load без фильтра** (первые запросы не отклоняются) — CONTROL-003;
4. вычесть `excludedNodeIds` (для retry/failover от ApiGateway);
5. при `preferredNodeId` — оставить только её, **сверив и `ControlToken`** (ordinal);
6. сортировка least-load: `InFlightRequests / CapacityWeight`, затем `InFlightRequests`, затем `Name`;
7. `node.Lease()` (++inflight) + создать `RouterLease`, вернуть `RouterNodeLease{LeaseId, NodeId, EndpointUrl, ControlToken}`.

`ReleaseRouterNodeAsync(leaseId)` — снять лиз и `node.Release()` (--inflight).

## Capability-aware routing

- `NodeModelCapabilityStore` ([файл](Application/Services/NodeModelCapabilityStore.cs)) — потокобезопасный `ConcurrentDictionary<nodeId, set<modelId>>`, читается на hot-path лок-фри. `IsEmpty`, `NodeSupportsModel`, `ReplaceAll` (атомарная замена снапшота).
- `CapabilityReplicaRefresher` ([файл](Application/Services/CapabilityReplicaRefresher.cs)) — `BackgroundService`, периодически (`CapabilityReplicaOptions.RefreshInterval`) тянет `GetCapabilitiesSnapshotAsync` из **CollectorService** по gRPC и `store.ReplaceAll`. **При сбое коллектора store не трогается** — роутинг использует устаревшие подсказки. Заменил прежний in-process Quartz-джоб.

## Health-чеки — `RouterHealthCheckWorker` ([файл](Application/Services/RouterHealthCheckWorker.cs))

`BackgroundService`: каждые `RouterHealthCheckOptions.Interval` обходит все enabled-ноды, `healthClient.CheckAsync` → `controlPlane.ApplyHealthAsync(status, latency, errorCode)`. **Нет cooldown/flapping policy** — нода мгновенно меняет статус (CONTROL-002). `RouterLeaseCleanupWorker` — отдельный воркер чистки протухших лизов.

## Admin API нод — `RouterNodeProvider` ([файл](Infrastructure/Http/RouterNodeProvider.cs))

HTTP-клиент к `/api/integrations/*` каждой ноды. **Аутентификация: `Authorization: Bearer <node.ControlToken>` + дублируется в `X-ORR-Node-Token`** — `control_token` берётся прямо из сущности, никакого резолва через config. Методы: provider quota (`GET .../providers/quota`), provider status (`PATCH .../providers/{id}/status`), integration keys CRUD (`POST/PATCH/DELETE .../keys[/{id}/status]`), bulk `POST .../keys/sync`. Ошибки оборачиваются в `RouterNodeProviderException` (статус + распарсенный `error` из тела). `CachedRouterNodeProvider` — кеширующая обёртка (`RouterNodeProviderCacheOptions`).

## Синхронизация ключей — `GrpcRouterControlPlaneService` ([файл](Infrastructure/Grpc/GrpcRouterControlPlaneService.cs))

- **`PushUserIntegrationKey`**: пушит ключ на все enabled-ноды (или одну `NodeId`) через `CreateIntegrationKeyAsync`; маппинг `(apiKeyId, nodeId, routerKeyId)` сохраняется в `control.integration_key_sync` через `repository.SaveIntegrationKeyMappingAsync`. Ошибки по нодам — log warning, не валят весь пуш.
- **`RevokeUserIntegrationKey`**: по маппингам → `UpdateIntegrationKeyStatusAsync(isActive:false)`. **Маппинг НЕ удаляет** — связь нужна для последующего полного удаления.
- **`DeleteUserIntegrationKey`**: `DeleteIntegrationKeyAsync` + `RemoveIntegrationKeyMappingsAsync`.
- **`SyncNodeIntegrationKeys`**: тянет все активные raw-ключи из **AuthService** (`ListAllActiveApiKeysRawAsync`) и bulk-пушит на ноду.
- `GetRouterNodeDetails`: агрегирует quota + integration keys с ноды, каждый под таймаутом 3с (`NodeDetailsProviderTimeout`), ошибки складывает в `ApiError`, не валит ответ.

## Частые заблуждения (НЕ повторять)

| Неверно | Верно |
|---|---|
| `control_token` в БД = имя секрета | `control_token` = **реальное значение токена**, шлётся напрямую `Bearer` |
| `RouterHealthChecks:NodeTokens` резолвит токен | Такой логики нет — `RouterNodeProvider` берёт `node.ControlToken` |
| Валидация `control_token` где-то в инфраструктуре | Уже в доменной сущности `RouterNode` — **не флагать как пропуск** |
| HealthPath `/v1/models` работает | 401 от ноды; нужно `/api/health` (dev-overlay патчит) |

**Прочие пробелы**: после рестарта in-flight счётчики сбрасываются, recovery лизов нет (CONTROL-004).

## БД и тесты

Миграции `db/migrations/control/` (3 файла), `ConnectionStrings:ControlDb`. PG-тесты override — `ORR_CONTROL_POSTGRES_TEST_CONNECTION`.
