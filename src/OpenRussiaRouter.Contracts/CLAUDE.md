# Contracts — CLAUDE.md

Общий проект: `.proto`-файлы (источник истины gRPC) + ручные C# DTO, разделяемые между сервисами. Ссылается только на BuildingBlocks. На него ссылаются все сервисы.

## ⚠️ Ключевой нюанс сборки

**`.proto` файлы НЕ компилируются в этом проекте.** В `OpenRussiaRouter.Contracts.csproj` нет `<Protobuf>`-элементов. Вместо этого **каждый сервис подключает нужные proto сам** и генерирует stubs в **свою** сборку:

```xml
<!-- например, ApiGateway.csproj -->
<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\auth.proto" GrpcServices="Client" Link="Protos\auth.proto" />
```

`GrpcServices` задаёт, что генерить:
| Значение | Кто использует |
|---|---|
| `Server` | сервис-владелец (генерит базовый класс `*Base`) — напр. Collector для `collector.proto` |
| `Client` | потребитель (генерит `*Client`) — напр. ApiGateway/WebApp/ControlPlane |
| `Both` | владелец, который сам себе и сервер, и клиент — Auth/Billing/control-plane в своих проектах |

Следствие: **сгенерированные gRPC-типы (`OpenRussiaRouter.Contracts.Grpc.<Service>.V1.*`) физически дублируются в каждой потребляющей сборке**, их нет в Contracts.dll. Версии `Google.Protobuf` (3.34.1) и `Grpc.Tools` (2.80.0) пинятся в `Directory.Packages.props`.

## Структура

| Папка | Содержимое |
|---|---|
| `Protos/auth.proto` | контракт AuthService (`GrpcServices="Both"` в Auth, `Client` у потребителей) |
| `Protos/billing.proto` | контракт BillingService |
| `Protos/control-plane.proto` | контракт ControlPlane |
| `Protos/collector.proto` | контракт CollectorService (`Server` в Collector, `Client` в ControlPlane/WebApp) |
| `Auth/AuthContracts.cs` | **ручные** C# DTO для Auth |
| `Billing/BillingContracts.cs` | ручные DTO (напр. `BalanceCheckResult`, `UsageEvent`, `PublicModel` — используются в `BillingApplicationService`) |
| `ControlPlane/ControlPlaneContracts.cs` | ручные DTO (`RouterNodeLease`, `RouterNodeSummary`, …) |

## Соглашения proto

- `syntax = "proto3"`, `package openrussiarouter.<service>.v1`, `option csharp_namespace = "OpenRussiaRouter.Contracts.Grpc.<Service>.V1"`.
- Имена сервисов: `AuthService`, `BillingService`, `RouterControlPlane`, `Collector`.
- В `collector.proto` rpc прокомментированы по потребителю (`GetCapabilitiesSnapshot` → ControlPlane replica; `ListNodeModelCatalog` → WebApp).

## Два вида контрактов — не путать

1. **proto-генерируемые** (`...Grpc...V1`) — транспорт gRPC между сервисами. Менять = править `.proto`.
2. **ручные `*Contracts.cs` DTO** — кросс-слойные модели приложения (фасады возвращают их, gRPC-адаптеры мапят в/из proto-типов). Это обычные record'ы, правятся как обычный C#.

## При добавлении нового gRPC-метода

1. Правишь `.proto` в `Protos/`.
2. Добавляешь `<Protobuf>` в csproj **каждого** нужного сервиса (сервер — `Server`/`Both`, клиенты — `Client`), если файл там ещё не подключён.
3. Ребилд → stubs появляются в этих сборках.
4. Реализуешь сервер в `Infrastructure/Grpc/<service>` и вызываешь из клиентов.

**Не класть сюда бизнес-логику** — только контракты/DTO. Общие хелперы (Money, TokenUsage, ошибки) — в BuildingBlocks.
