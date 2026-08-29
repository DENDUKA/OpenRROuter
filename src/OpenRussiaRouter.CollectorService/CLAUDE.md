# CollectorService — CLAUDE.md

Периодический опрос нод 9Router и каталога OpenRouter, in-memory кеш снапшотов, отдача снапшотов
по gRPC + Prometheus-метрики квот. **Своей БД нет** — всё in-memory, состояние теряется при
рестарте (by design; первый снапшот появится только после первого цикла опроса).

Слоёв Domain/Repository здесь нет: `Application/*` (воркеры, сборщики, сторы, проекции) +
`Infrastructure/Grpc` (единственный gRPC-фасад).

## Два эндпоинта, оба обязательны

| Kestrel-endpoint | Протокол | Что отдаёт |
|---|---|---|
| `Http` (в compose/k8s `8080`) | HTTP/1.1 | `/health` (`AddHealthChecks`+`MapHealthChecks`), `/metrics` (`MapOpenTelemetryEndpoints`), `/error` |
| `Grpc` (в compose/k8s `8081`) | HTTP/2 cleartext | `GrpcCollectorService` |

Порты приходят **только из env** (`Kestrel__Endpoints__{Http,Grpc}__{Url,Protocols}`) —
в [appsettings.json](appsettings.json) секции `Kestrel` нет. Маппинг на хост в compose:
`5005:8081` (gRPC) и `5006:8080` (HTTP, по нему health-чек в `update-orr.sh`).

- **Скрейпить `/metrics` можно только на HTTP-порту**: 8081 — h2c, Prometheus говорит HTTP/1.1
  и получит 400 (см. комментарий в `deploy/compose/prometheus.prod.yml`).
- **В k8s Service `collector:8080` → targetPort `8081`**, то есть кластерный адрес `collector:8080`
  ведёт на **gRPC** ([infra/kustomize/base/services/collector.yaml](../../infra/kustomize/base/services/collector.yaml)),
  а потребители в compose ходят на `collector:8081`. Не путать при правке манифестов.

## Регистрация ([Startup.cs](Infrastructure/Startup/Startup.cs))

`AddGrpc` + `AddHealthChecks`; синглтоны сторов/сборщиков; `TimeProvider.System`;
`INodeListProvider`→`NodeListProvider`; `HttpClient` для `NodeAdminClient` (**timeout 30 с**);
два gRPC-клиента (keep-alive + `GrpcDeadlineInterceptor`, `GrpcServices:CallDeadlineSeconds`, дефолт 30):
**ControlPlane** (`GrpcServices:ControlPlane`, дефолт `:5003`) и **Billing**
(`GrpcServices:Billing`, дефолт `:5002`); два `HostedService`: `NodePollingWorker` и
`OpenRouterCatalogPoller`. Опции (`CollectorOptions`, `OpenRouterCatalogOptions`) —
`ValidateDataAnnotations().ValidateOnStart()`, невалидный конфиг роняет старт.

`ProviderQuotaMeter` регистрируется в DI, но **инстанцируется вручную в
[Program.cs](Program.cs)** (`GetRequiredService<ProviderQuotaMeter>()`): без этой строки
observable-гейджи никогда не создаются и метрик квот в `/metrics` просто нет.

## Структура

| Папка | Содержимое |
|---|---|
| `Application/Collection` | `NodePollingWorker`, `NodeCatalogCollector`, `CollectorSnapshotStore`, `CollectorSnapshot`, `NodeCatalogResult`/`ModelObservation`, `CatalogProjection`, `NodeListProvider`, `CollectorOptions`, `CollectorNode` |
| `Application/NodeAdmin` | `NodeAdminClient` (`INodeAdminClient`) — HTTP к admin API ноды, контракты, `NodeAdminException` |
| `Application/Billing` | `GrpcBillingModelCatalogProvider` (`IBillingModelCatalogProvider`), `BillingCatalogModel` — **весь** каталог моделей биллинга, включая выключенные |
| `Application/OpenRouter` | `OpenRouterCatalogPoller`, `OpenRouterCatalogClient`, `OpenRouterCatalogStore`/`OpenRouterCatalogSnapshot`, контракты, опции |
| `Application/Quota` | `NodeQuotaCollector`, `ProviderQuotaMeter`, `QuotaSnapshotStore`, `QuotaMeasurement`, `QuotaStatusMeasurement`+`QuotaUnavailableReasons`, `ActiveModelQuotaMeasurement`, `NodeModelCatalogEntry`+`ModelBillingStates`, `NodeQuotaResult` |
| `Infrastructure/Grpc` | `GrpcCollectorService` — отдаёт снапшоты |

## Конфигурация ([appsettings.json](appsettings.json))

| Ключ | Дефолт | Смысл |
|---|---|---|
| `Collector:NodePollInterval` | `00:01:00` | пауза между циклами опроса (5с…1ч) |
| `Collector:NodeListRefreshInterval` | `00:01:00` | как часто перечитывать список нод из ControlPlane (5с…1ч) |
| `Collector:ActiveModelsRefreshInterval` | `00:05:00` | TTL кеша активных моделей из Billing (30с…6ч) |
| `Collector:PerNodeTimeout` | `00:00:30` | таймаут на одну ноду, linked CTS (1с…1мин) |
| `OpenRouter:Enabled` | `true` | `false` — воркер тихо ничего не делает |
| `OpenRouter:BaseUrl` | `https://openrouter.ai/` | базовый адрес, запрос `GET api/v1/models` |
| `OpenRouter:RefreshInterval` | `00:30:00` | период обновления каталога (1мин…6ч) |

**Ключ OpenRouter не нужен** — `OpenRouterCatalogClient` ходит на публичный `api/v1/models` без
авторизации. `OPENROUTER_API_KEY` в репозитории — это секрет **ноды f9router**, к Collector'у
отношения не имеет.

## Цикл опроса — `NodePollingWorker.RunOnceAsync` ([файл](Application/Collection/NodePollingWorker.cs))

Каждые `NodePollInterval` (лог-скоуп `PollCycleId`):

1. **Список нод — с собственным кешем**: `INodeListProvider.GetNodesAsync` вызывается только
   когда кеш пуст или старше `NodeListRefreshInterval`. Если refresh упал, а кеш есть —
   работаем на устаревшем списке (log warning); если списка не было никогда — выход,
   **предыдущий снапшот сохраняется**.
2. Фильтр `NodeCatalogCollector.IsRoutableForCatalog`: `Enabled` **И** (нода ещё ни разу не
   health-чекалась **ИЛИ** её `Status != "Unhealthy"`). `Enabled` — это «статус не `Disabled`»
   (см. [NodeListProvider.cs](Application/Collection/NodeListProvider.cs), ноды с невалидным
   `EndpointUrl` отбрасываются там же).
3. Параллельно (`Task.WhenAll`) каталог каждой ноды → атомарно `store.Set(new CollectorSnapshot(...))`.
4. Параллельно квоты каждой ноды + активные модели из Billing → обогащение
   (`ModelIds` по `ConnectionId`, `ActiveModelQuotaMeasurement`) → `quotaStore.Set(...)`.

Ошибка цикла логируется и не убивает воркер. Источник времени — `TimeProvider`
(в тестах — fake, см. `NodePollingWorkerTests`).

## Сбор каталога ноды — `NodeCatalogCollector.CollectAsync` ([файл](Application/Collection/NodeCatalogCollector.cs))

- per-node таймаут (`PerNodeTimeout`) через linked CTS;
- основной путь `GET /api/integrations/providers/with-models` (`ListProvidersWithModelsAsync`):
  только `IsActive`-connection и `Enabled`-модели, тянутся `PublicId`, тип, `Sources`
  и `Caps` (reasoning / `ThinkingFormat` / `ThinkingCanDisable` / `ThinkingOptions`);
- **fallback только на HTTP 404**: `GET /v1/models` (`ListModelsAsync`), из него известен
  лишь upstream-id;
- затем **всегда** `GET /api/integrations/combos` (`AddCombosAsync`) — combo-алиасы
  добавляются как отдельные routable-модели, если такого id ещё нет. Падение combos-запроса
  не ломает каталог (log warning, отдаём то, что собрали);
- `Rank` наблюдения (используется `CatalogProjection` для выбора «лучшего» описания):
  **1** — fallback `/v1/models`, **2** — модель обычного connection,
  **3** — модель combo-connection и сам combo-алиас (алиас важнее прямого маршрута);
- транзиентные ошибки (`NodeAdminException`/`HttpRequestException`/`InvalidOperationException`/
  таймаут) → `NodeCatalogResult{Unreachable:true}` с диагностикой request/response
  (тело обрезается до 1024 символов). Нода попадёт в `UnreachableNodes` снапшота.

**`PerNodeTimeout` больше 30 с не имеет смысла**: `HttpClient` для `NodeAdminClient`
зарегистрирован с `Timeout = 30s`, он сработает раньше.

Авторизация к ноде — `Authorization: Bearer <control_token>` **и** дубль в `X-ORR-Node-Token`;
токен берётся из `CollectorNode.ControlToken`, то есть из `control.router_nodes.control_token`
как есть (см. корневой CLAUDE.md, «Два типа токенов»). Пустой токен → `InvalidOperationException`,
нода помечается unreachable.

## Квоты — `NodeQuotaCollector` ([файл](Application/Quota/NodeQuotaCollector.cs))

`GET /api/integrations/providers/quota?…` (`includeInactive: false`), `usage` парсится как
сырой `JsonElement`: либо вложенный объект `quotas` (по записи на каждую квоту), либо плоские
поля (`remaining` / `total`-`used`, процент из `remainingPercentage` / `limit` / `total`).
Кроме процента забираются потолок окна (`Total` = `total` ?? `limit`) и момент обнуления
(`ResetAt` ← `resetAt`, ISO-8601, парсится `InvariantCulture` + `RoundtripKind`).

**Длину окна («5-часовой лимит», «недельный») не отдаёт ни один провайдер** — в ответе есть
только `resetAt`. Сам 9Router в Quota Tracker тоже показывает лишь обратный отсчёт («in 5h 0m»).
Поэтому наружу отдаётся момент сброса, а не выведенный из него тип: горизонт по мере
расходования окна уменьшается, и после сброса он равен длине окна, а перед сбросом — почти нулю.
Оценка типа живёт в дашборде (подпись «окно ~суточный») и честно помечена как приблизительная.

Ключевой инвариант: **каждое подключение обязано быть видимо, даже если чисел нет.**
Кроме `QuotaMeasurement` возвращается `QuotaStatusMeasurement` с `Reporting` и `Reason` из
**закрытого** списка `QuotaUnavailableReasons` (`ok`, `node_unreachable`, `no_usage`,
`empty_quotas`, `no_quota_fields`). Сырое сообщение ноды в метку класть нельзя —
неограниченная кардинальность. Нода недоступна целиком → одна строка-статус на ноду
с `node_unreachable` (на этом построен алерт в `deploy/compose/grafana/provisioning/alerting/rules.yaml`).

`NodePollingWorker` дополнительно сшивает квоты с админскими моделями Billing
(`IBillingModelCatalogProvider`, матч по `RouterModelId` ↔ `UpstreamId`/`PublicId` наблюдения —
общий хелпер `FindCatalogModels`): модель-специфичная квота, иначе общая по connection, иначе
среднее — результат `ActiveModelQuotaMeasurement`. Сбой Billing не ломает цикл:
`GrpcBillingModelCatalogProvider` отдаёт свой прошлый кеш (или пустой список).

**`IBillingModelCatalogProvider` отдаёт и выключенные модели** (`BillingCatalogModel.Enabled`) —
без них выключенная у нас модель неотличима от модели, которой в каталоге нет вовсе.
Фильтр по `Enabled` стоит у потребителя: `BuildActiveModelMeasurements` обязана его сохранять,
иначе `orr_active_model_quota_percent` начнёт включать то, что мы не продаём.

`BuildModelCatalog` в том же воркере строит `QuotaSnapshot.ModelCatalog` — по записи на
(нода, подключение, модель): совпало с каталогом → `enabled`/`disabled` и публичный `ModelId`,
не совпало → `absent` и routable id ноды. Unreachable-ноды исключаются, дубли (прямой маршрут +
combo-алиас) схлопываются по `(NodeId, ConnectionId, ModelId)` — метка Prometheus обязана быть
уникальной.

**Экспортёр дописывает `unit` к имени метрики.** `orr_provider_quota` объявлен с
`unit: "tokens"` и виден в Prometheus как **`orr_provider_quota_tokens`** — под этим именем его
и надо искать в PromQL и дашбордах. `orr_provider_quota_percent` не удвоился только потому,
что имя уже кончается на единицу измерения. Новым гейджам unit не задавай, если он не должен
попасть в имя: так сделаны `orr_provider_quota_limit` и `orr_provider_quota_reset_seconds`.

Метрики (`ProviderQuotaMeter`, observable gauges, meter `OpenRussiaRouter`):
`orr_provider_quota` (→ `_tokens`), `orr_provider_quota_limit` (потолок окна),
`orr_provider_quota_reset_seconds` (unix-время сброса **в значении**, не в метке: время
в метке = новая серия на каждый сброс и неограниченная кардинальность),
`orr_provider_quota_percent`, `orr_provider_quota_status`,
`orr_model_active`, `orr_model_quota_percent`, `orr_active_model_quota_percent`,
`orr_node_model_catalog` (значение всегда 1, смысл в метке `billing_state` ∈
`enabled|disabled|absent`, закрытый список `ModelBillingStates`; кардинальность =
ноды × подключения × модели).
Метки: `node_id`/`provider`/`connection_id`/`account_name`/`quota_name`/`model_id`/
`router_model_id`/`reason`/`billing_state` — все ограниченной кардинальности,
**user/api-key сюда не добавлять**.

`orr_node_model_catalog` — единственный источник для дашборда `ORR Nodes & Provider Limits`
в части «какие модели нода отдаёт и что из них мы продаём»: в Postgres связки
нода↔подключение↔модель нет вообще.

## OpenRouter — `OpenRouterCatalogPoller` ([файл](Application/OpenRouter/OpenRouterCatalogPoller.cs))

Тянет каталог (slug, имя, автор, описание, `context_length`, input-модальности, цены строками
как есть) в `OpenRouterCatalogStore`. `OpenRouterCatalogClient` запрашивает каталог через активные
ноды 9Router (`GET /api/integrations/openrouter/models` с `Authorization: Bearer <control_token>`)
с фолбэком на прямой запрос к `openrouter.ai`. **Пустой/упавший fetch не затирает снапшот** (log warning),
`Available` остаётся `false` до первой успешной загрузки — потребитель по этому флагу понимает,
что данных не было никогда, и включает свой фолбэк.

## Сторы (lock-free) и gRPC-отдача

- `CollectorSnapshotStore` / `QuotaSnapshotStore` / `OpenRouterCatalogStore` — `volatile`-ссылка,
  запись атомарно подменяет ссылку, читатели всегда видят консистентный снапшот
  ([CollectorSnapshotStore.cs](Application/Collection/CollectorSnapshotStore.cs)).
- `CatalogProjection` ([файл](Application/Collection/CatalogProjection.cs)) — чистые проекции без I/O:
  routable model id = `PublicId`, иначе `UpstreamId`; unreachable-ноды исключаются;
  в merged-каталоге поля выбираются по большему `Rank`, `SourceProviders`/`NodeIds` объединяются.
- `GrpcCollectorService` ([файл](Infrastructure/Grpc/GrpcCollectorService.cs)), контракт —
  [collector.proto](../OpenRussiaRouter.Contracts/Protos/collector.proto):

| RPC | Потребитель |
|---|---|
| `GetCapabilitiesSnapshot` | ControlPlane `CapabilityReplicaRefresher` (capability-routing) |
| `ListNodeModelCatalog` | WebApp `CollectorProvider`, `ReasoningEnrichmentWorker` (+`UnreachableNodes` с диагностикой) |
| `GetNodeModels` | WebApp (`/admin/nodes/{id}`); неизвестная/unreachable нода → `Error`, не gRPC-ошибка |
| `GetOpenRouterCatalog` | WebApp `Services/Enrichment/OpenRouterCatalogClient` (enrich моделей в админке) |
| `GetProviderQuota` | **потребителей в репозитории нет** — квоты наблюдаются через Prometheus |

`GetProviderQuota` к тому же **лоссовый**: в proto уезжает только `Remaining`, а `Percent`,
`AccountName`, `QuotaName`, `ModelIds`, `ActiveModelMeasurements` и `Statuses` живут лишь
в метриках. Если понадобится отдать их наружу — править `.proto`, не «донастраивать» ответ.

## Подводные камни

- **Чистый кеш-сервис**: воркеры наполняют синглтон-сторы, gRPC отдаёт текущий снапшот.
  Никакой записи в БД, никаких миграций, `ConnectionStrings` в конфиге нет.
- **gRPC-сервис не защищён s2s-токеном** (`ServiceAuthInterceptor` не подключён) — доступ
  ограничен только сетью/NetworkPolicy. Это осознанно: данные read-only и не пользовательские.
- **k8s NetworkPolicy** (`infra/kustomize/base/infra/network-policies.yaml`): ingress к collector
  разрешён от control-plane и webapp; сам collector попадает в billing/control-plane по
  intra-namespace egress. Но **collector НЕ входит в `allow-external-egress`** — при
  default-deny egress запрос к `openrouter.ai` из dev-кластера не уйдёт; ноды достижимы,
  только пока они внутри namespace (`f9router*`). Новому исходящему направлению нужна политика,
  иначе gRPC/HTTP умирает по таймауту (errno 110).
- **`Microsoft.NET.Sdk`, а не `Sdk.Web`** ([csproj](OpenRussiaRouter.CollectorService.csproj)):
  `appsettings*.json` подключены как `Content` вручную — при добавлении новых конфиг-файлов
  проверь, что они попадают в образ. Proto: `collector.proto` — `GrpcServices="Server"`,
  `billing.proto`/`control-plane.proto` — `Client`.
- Тесты — `tests/OpenRussiaRouter.CollectorService.Tests` (xUnit + Moq + FluentAssertions,
  без Docker/БД): проекции, стор, `NodeCatalogCollector` (404-fallback, combos, unreachable),
  `NodePollingWorker` (кеш списка нод, stale-reuse), `NodeQuotaCollector` (статусы), gRPC-фасад,
  OpenRouter-клиент.
# Reasoning catalog contract (2026-08-29)

`ListNodeModelCatalog` переносит точный список `caps.reasoningEfforts` из 9Router как
`UpstreamModelEntry.reasoning_efforts`. Не выводи его заново из `thinkingOptions`: последние
нужны для обратной совместимости и UI-диагностики, а не являются списком публичных effort.
Для combo additive-поле `combo_reasoning_members` передаёт fallback-модели и их уровни; итоговый
`reasoning_efforts` уже является пересечением, вычисленным 9Router. Проекция выбирает
детерминированный снимок одной доступной node, а не объединяет уровни между nodes.
