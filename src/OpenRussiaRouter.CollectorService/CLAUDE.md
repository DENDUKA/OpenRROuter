# CollectorService — CLAUDE.md

Периодический опрос нод 9Router и OpenRouter, in-memory кеш снапшотов, отдача снапшотов по gRPC. **gRPC-сервер (порт 5005)** + HTTP `/health`. **БД нет** — всё in-memory, состояние теряется при рестарте (by design).

## Регистрация ([Startup.cs](Infrastructure/Startup/Startup.cs))

`AddGrpc`; синглтоны сторов и коллекторов; `INodeListProvider`→`NodeListProvider`; `HttpClient` для `NodeAdminClient` (timeout 10с); gRPC-клиент к **ControlPlane** (`GrpcServices:ControlPlane`, дефолт 5003, keep-alive + `GrpcDeadlineInterceptor`); два `HostedService`: `NodePollingWorker` и `OpenRouterCatalogPoller`. Опции (`CollectorOptions`, `OpenRouterCatalogOptions`) валидируются data-annotations — невалидный конфиг роняет старт.

## Структура

| Папка | Содержимое |
|---|---|
| `Application/Collection` | Опрос каталога нод: `NodePollingWorker`, `NodeCatalogCollector`, `CollectorSnapshotStore`, `CatalogProjection`, `NodeListProvider` |
| `Application/NodeAdmin` | `NodeAdminClient` (`INodeAdminClient`) — HTTP к admin API ноды, контракты |
| `Application/OpenRouter` | `OpenRouterCatalogPoller`, `OpenRouterCatalogClient`, `OpenRouterCatalogStore`, опции |
| `Application/Quota` | `NodeQuotaCollector`, `ProviderQuotaMeter`, `QuotaSnapshotStore`, `QuotaMeasurement` |
| `Infrastructure/Grpc` | `GrpcCollectorService` — отдаёт снапшоты |

## Цикл опроса — `NodePollingWorker.RunOnceAsync` ([файл](Application/Collection/NodePollingWorker.cs))

Каждые `CollectorOptions.NodePollInterval`:
1. `nodeListProvider.GetNodesAsync()` — список нод из ControlPlane по gRPC. **При недоступности ControlPlane — предыдущий снапшот сохраняется** (log warning, выход);
2. фильтр `NodeCatalogCollector.IsRoutableForCatalog` (enabled И не `Unhealthy`);
3. параллельно (`Task.WhenAll`) собрать каталог каждой ноды → атомарно `store.Set(new CollectorSnapshot(...))`;
4. параллельно собрать quota → `quotaStore.Set(...)`.

`OpenRouterCatalogPoller` — отдельный воркер, тянет каталог OpenRouter (цены/модальности/контекст) в `OpenRouterCatalogStore`.

## Сбор каталога ноды — `NodeCatalogCollector.CollectAsync` ([файл](Application/Collection/NodeCatalogCollector.cs))

- per-node таймаут (`CollectorOptions.PerNodeTimeout`) через linked CTS;
- основной путь: `ListProvidersWithModelsAsync` → берёт только активные connection и `Enabled` модели, `Rank=2` (предпочтительные, есть `PublicId`/тип/источники);
- **fallback на HTTP 404**: `ListModelsAsync` (`/v1/models`), `Rank=1` (только upstream-id);
- транзиентные ошибки (`NodeAdminException`/`HttpRequestException`/timeout) → `NodeCatalogResult{Unreachable:true}` с диагностикой request/response (тело обрезается до 1024 симв.). Нода попадёт в `UnreachableNodes` снапшота.

## Сторы (lock-free) и gRPC-отдача

- `CollectorSnapshotStore` / `QuotaSnapshotStore` / `OpenRouterCatalogStore` — `volatile`-ссылка, запись атомарно подменяет ссылку, читатели всегда видят консистентный снапшот ([CollectorSnapshotStore.cs](Application/Collection/CollectorSnapshotStore.cs)).
- `GrpcCollectorService` ([файл](Infrastructure/Grpc/GrpcCollectorService.cs)) проецирует текущий снапшот через `CatalogProjection`:
  - **`GetCapabilitiesSnapshot`** → node→modelIds (потребляет `CapabilityReplicaRefresher` в ControlPlane для capability-routing);
  - `ListNodeModelCatalog` → каталог моделей + `UnreachableNodes` с диагностикой;
  - `GetNodeModels`, `GetProviderQuota`, `GetOpenRouterCatalog`.

## Подводные камни

- **Чистый кеш-сервис**: воркеры наполняют синглтон-сторы, gRPC отдаёт текущий снапшот. Никакой записи в БД.
- **Главный потребитель** — ControlPlane (`GetCapabilitiesSnapshot`); также WebApp (`CollectorProvider`).
- **k8s**: новый сервис в `orr-dev` требует **NetworkPolicy в обе стороны**, иначе default-deny роняет gRPC с timeout (errno 110). См. память `project_collector_networkpolicy`.
- OpenRouter: секция `OpenRouter` (`BaseUrl` дефолт `https://openrouter.ai/`, ключ `OPENROUTER_API_KEY`).
