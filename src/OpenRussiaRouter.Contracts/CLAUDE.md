# Contracts — CLAUDE.md

Общий проект: `.proto`-файлы (**источник истины gRPC**) + небольшой набор ручных C# DTO, разделяемых между сервисами. Ссылается только на BuildingBlocks. На него ссылаются все сервисы (`ApiGateway`, `AuthService`, `BillingService`, `ControlPlane`, `ContentService`, `CollectorService`, `WebApp`).

## ⚠️ Ключевой нюанс сборки

**`.proto` файлы НЕ компилируются в этом проекте.** В `OpenRussiaRouter.Contracts.csproj` нет ни `<Protobuf>`-элементов, ни `Grpc.Tools`/`Google.Protobuf` — это обычная библиотека с одним `ProjectReference` на BuildingBlocks. Вместо этого **каждый сервис подключает нужные proto сам** и генерирует stubs в **свою** сборку:

```xml
<!-- например, ApiGateway.csproj -->
<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\auth.proto" GrpcServices="Client" Link="Protos\auth.proto" />
```

`GrpcServices` задаёт, что генерить:

| Значение | Что генерится | Кто использует |
|---|---|---|
| `Both` | и `*Base`, и `*Client` | сервис-владелец, который сам себе клиент: Auth (`auth.proto`), Billing (`billing.proto`), ControlPlane (`control-plane.proto`), Content (`content.proto`) |
| `Server` | только базовый класс `*Base` | Collector для `collector.proto` (клиента себе не делает) |
| `Client` | только `*Client` | потребители (ApiGateway, WebApp, ControlPlane, Collector) |

Следствие: **сгенерированные типы (`OpenRussiaRouter.Contracts.Grpc.<Service>.V1.*`) физически дублируются в каждой потребляющей сборке**, в `Contracts.dll` их нет. Версии `Google.Protobuf` (3.34.1) и `Grpc.Tools` (2.80.0) пинятся в `Directory.Packages.props`.

## Структура

| Файл | Содержимое | Сервер | Клиенты |
|---|---|---|---|
| `Protos/auth.proto` | сервис `AuthService` | AuthService (`Both`) | ApiGateway, WebApp, ControlPlane |
| `Protos/billing.proto` | сервис `BillingService` | BillingService (`Both`) | ApiGateway, WebApp, CollectorService |
| `Protos/control-plane.proto` | сервис `RouterControlPlane` | ControlPlane (`Both`) | ApiGateway, WebApp, CollectorService |
| `Protos/collector.proto` | сервис `Collector` | CollectorService (`Server`) | ControlPlane, WebApp |
| `Protos/content.proto` | сервис `ContentService` | ContentService (`Both`) | WebApp |
| `Auth/AuthContracts.cs` | ручные DTO: `ValidateApiKeyResult`, `ApiKeySummary` |
| `Billing/BillingContracts.cs` | ручные DTO: `BalanceCheckResult`, `PublicModel`, `UsageEvent`, `GrantStartBalanceResult`, `PromotionSettings`, `GrantFirstTopUpBonusResult` |
| `ControlPlane/ControlPlaneContracts.cs` | ручной DTO: `RouterNodeLease` (единственный) |

Серверные адаптеры живут в сервисах-владельцах (`Infrastructure/Grpc/Grpc*Service.cs`), клиентские обёртки — в `WebApp/Services/{Auth,Billing,ControlPlane,Collector,Content}/*Provider.cs` и `ApiGateway/{Auth,Billing,ControlPlane}/Grpc*Provider.cs`.

## Обзор контрактов

### `auth.proto` → `AuthService` (порт 5001)

Один сервис, ~25 RPC, группы:
- **валидация ключа** — `ValidateApiKey` (горячий путь ApiGateway, возвращает `user_id`/`api_key_id`/`role`);
- **сессии и пароли** — `Login`, `RegisterUser`, `VerifyEmail`, `ResendVerification`, `ChangePassword`, `RequestPasswordReset`, `ResetPassword`. `VerifyEmail` и внешние логины возвращают тот же `LoginResponse`;
- **внешний логин** — `LoginOrRegisterWithGoogle` (legacy-специфичный) и общий `LoginOrRegisterWithExternalProvider` (`provider` + `provider_sub`);
- **инвайты** — `CreateInvite` / `CompleteInvite`;
- **API-ключи пользователя** — `CreateApiKey`, `Rename/Enable/Disable/DeleteApiKey`, `ListApiKeys`;
- **служебные выгрузки ключей для нод/синка** — `ListActiveApiKeyHashes`, `GetFirstActiveApiKeyRaw`, `ListAllActiveApiKeysRaw`, `ListAllNonDeletedApiKeysRaw` (последний использует ControlPlane при синке ключей на ноды);
- **пользователи** — `ListUsers`, `GetUser`, `UpdateUser` (`display_name`, `max_api_keys`, `display_currency`).

`RegisterUserResponse` / `ResendVerificationResponse` / `RequestPasswordResetResponse` отдают **сырой код** вызывающему (WebApp отправляет письмо) — в БД он в clear text не лежит.

### `billing.proto` → `BillingService` (порт 5002)

Самый большой контракт (~40 RPC), группы:
- **горячий путь gateway** — `CheckBalanceAndModel` (резолв публичного `model_id` → `router_model_id`, флаги reasoning), `RecordUsage` (+ сообщение `TokenUsage`);
- **spend-лимиты ключей** — `Get/UpsertApiKeyLimitSettings`, `ListApiKeyLimitSummaries`;
- **каталог моделей и цены** — `ListPublicModels`, `ListModels`, `GetModel`, `UpsertModelPrice`;
- **провайдеры моделей** — `ListModelProviders`, `GetModelProvider`, `Upsert/DeleteModelProvider` (при этом сообщения называются `ListProvidersRequest`/`GetProviderRequest`/… — имена RPC и сообщений тут расходятся, это нормально);
- **теги** — `ListTags`, `UpsertTag`, `DeleteTag`;
- **баланс и ручные начисления** — `GetBalance`, `CreateVirtualTopUp`, `ListTopUps`, `ListAllTopUps`;
- **usage / активность / аномалии** — `ListUsage`, `ListActivityByToken`, `GetActivitySummary`, `ListBillingAnomalies`, `ListAdminRequests`, `GetAdminRequestsSummary`;
- **платежи и FX** — `GetFxQuote`, `CreatePayment`, `AttachProviderRef`, `ConfirmPaymentByProvider`, `ApplyPaymentEvent`, `GetPayment` (возвращает `PaymentRecord` напрямую, без обёртки-Response), `ListPayments`, `AcquireStalePayments`;
- **промо и настройки платформы** — `GrantStartBalance`, `HasUserHadPaidTopUp`, `GetPromotionSettings`, `GrantFirstTopUpBonus`, `Get/SetPlatformSetting`.

### `control-plane.proto` → `RouterControlPlane` (порт 5003)

- **лизинг нод** — `SelectRouterNode` (принимает **`RouterModelId`**, `preferred_node_id`, `excluded_node_ids`), `ReleaseRouterNode`;
- **CRUD нод** — `Register/Update/Disable/Enable/DeleteRouterNode`, `UpdateRouterNodeControlToken`;
- **чтение** — `ListRouterNodes` (→ `RouterNodeSummary`, **без** control-token), `ListRouterNodesForInternalConsumers` (→ `RouterNodeAdminSummary`, **с** `control_token`), `GetRouterNodeDetails` (нода + провайдеры/квоты + integration-ключи ноды);
- **синк пользовательских ключей на ноды** — `PushUserIntegrationKey`, `UpdateUserIntegrationKeyStatus`, `DeleteUserIntegrationKey`, `SyncNodeIntegrationKeys`, `CheckNodeIntegrationKeysSync`.

Два важных момента:
- в `RouterNodeSummary` поле 7 помечено `reserved 7; reserved "control_token";` — токен вынесли в отдельное сообщение для доверенных внутренних потребителей. **Номер 7 не переиспользовать.**
- `RevokeUserIntegrationKeyResponse` — **осиротевшее сообщение**: ни одного RPC с таким именем нет, отзыв делается через `UpdateUserIntegrationKeyStatus(is_active:false)`. Не искать несуществующий `RevokeUserIntegrationKey`.

### `collector.proto` → `Collector` (порт 5005)

Только чтение in-memory снапшотов, rpc прокомментированы по потребителю:
- `GetCapabilitiesSnapshot` — node → routable model ids, потребляет локальная capability-реплика ControlPlane;
- `ListNodeModelCatalog` — обогащённый сводный каталог (`UpstreamModelEntry`) + `UnreachableNodeInfo` с диагностикой, потребляет WebApp;
- `GetNodeModels` — модели одной ноды;
- `GetProviderQuota` — измерения квот провайдеров;
- `GetOpenRouterCatalog` — каталог OpenRouter (цены строками, `available=false` пока ни разу не скачался).

### `content.proto` → `ContentService` (порт 5007)

Явно разделён комментариями на два блока:
- **публичный** (без s2s-токена): `ListTools`, `GetTool`, `GetToolLogo`, `GetToolImage`, `ListToolCategories`;
- **админский** (s2s, см. `ServiceAuth:MethodPolicies`): `ListToolsForAdmin`, `GetToolForAdmin`, `UpsertTool`, `DeleteTool`, `SetToolPublished`, `ListToolImages`, `UploadToolImage`, `DeleteToolImage`, `UpsertToolCategory`, `DeleteToolCategory`.

`ToolSummary` — карточная проекция (без markdown-тел и байтов логотипа), `Tool` — полная сущность; картинки/логотипы ходят как `bytes`.

## Соглашения proto

- `syntax = "proto3"`, `package openrussiarouter.<service>.v1`, `option csharp_namespace = "OpenRussiaRouter.Contracts.Grpc.<Service>.V1"`.
- Имена сервисов: `AuthService`, `BillingService`, `RouterControlPlane`, `Collector`, `ContentService`.
- **Деньги — строки**, не double: `balance_usd`, `daily_limit_usd`, `input_price_per_1m_usd`, … Мапятся через `decimal.Parse/ToString(CultureInfo.InvariantCulture)` в `MoneyAmount` (`BuildingBlocks/Money`). Ставить `double`/`float` под деньги нельзя — потеря точности относительно `numeric(18,6)`.
- Ручные справочные цены OpenRouter (`openrouter_*_price_per_1m_usd`) передаются строками в
  `PublicModel`, `UpsertModelPriceRequest` и `ModelDetail`; пустая строка означает `null`, а `"0"` —
  заполненную нулевую цену. Поля additive и не заменяют реальные `*_price_per_1m_usd` платформы.
- **Время**: в `auth.proto` — `int64 *_unix_time_seconds`; в billing/control-plane/content/collector — **строки ISO-8601 round-trip (`"O"`, InvariantCulture)**, `*_at_utc`. Внутри одного файла формат не смешивать.
- **Ошибки — в теле ответа**, а не gRPC status: пара `bool <succeeded|allowed|found|is_created|…>` + `string error_code` (пустая строка = нет ошибки). Structured gRPC error details в проекте нет (PLATFORM-002).
- **Отсутствие значения = пустая строка / 0** почти везде; `optional` используется только в `auth.proto` (`UserInfo.display_name`, `display_currency`, `UpdateUserRequest.*`), где нужно различать «не задано» и «очистить».
- **Курсорная пагинация** в billing-листингах: `next_cursor` — склейка `"{createdAtUtc:O}|{id}"`, парсится на стороне сервиса.

## Два вида контрактов — не путать

1. **proto-генерируемые** (`...Grpc.<Service>.V1.*`) — транспорт gRPC между сервисами. Менять = править `.proto`.
2. **ручные `*Contracts.cs` DTO** — кросс-слойные модели приложения: application-сервисы владельца возвращают их, а gRPC-адаптер мапит в/из proto-типов (и клиентские обёртки — обратно). Обычные `sealed record`, правятся как обычный C#. Живые потребители: `BillingApplicationService` + `GrpcBillingService` + `WebApp/Services/Billing/BillingProvider` (Billing DTO), `AuthApiKeyValidator` + `AuthProvider` (Auth DTO), `RouterControlPlaneService` (`RouterNodeLease`).

Ручные DTO для Collector и Content **не заводились** — там работают напрямую с proto-типами. И отдельно: **WebApp почти не переиспользует эти DTO** — у него свой набор view-моделей (`Services/Billing/*Info.cs`, `Services/Auth/UserInfo.cs`, `Services/Content/ContentContracts.cs`); новый контракт для страницы кабинета/админки скорее место там, чем здесь.

## Как добавить поле в существующее сообщение

1. Взять **следующий свободный номер**, не переиспользовать удалённые (см. `reserved` в `RouterNodeSummary`). Номера — часть wire-формата; переиспользование = молчаливая порча данных при rolling-деплое.
2. Удаляешь поле — не просто стираешь строку, а добавляешь `reserved <номер>;` и `reserved "<имя>";`.
3. Новое поле должно быть безопасно «пустым»: старый сервер вернёт для него default, и клиент это увидит. Прод-деплой сервисов не атомарен — оба направления совместимости обязательны.
4. Не менять тип уже существующего поля и не переименовывать `package`/имя сервиса — полное имя метода (`/openrussiarouter.<pkg>.v1.<Service>/<Rpc>`) зашито в маршрутизацию и в s2s-политики.

## Как добавить новый RPC

1. Правишь `.proto` в `Protos/` — добавляешь `rpc` + request/response сообщения.
2. Проверяешь, что `.proto` подключён `<Protobuf>` в csproj **каждого** нужного проекта: владелец — `Both`/`Server`, потребители — `Client`. Новый файл = новая строка в csproj каждого участника (и `ProjectReference` на Contracts).
3. Ребилд → stubs появляются в этих сборках (`obj/<Config>/net8.0/*.cs`, в git не попадают).
4. Реализуешь метод в серверном адаптере владельца (`Infrastructure/Grpc/Grpc*Service.cs`), при необходимости добавляешь ручной DTO и метод в application-слой.
5. **Если RPC админский/межсервисный** — добавь его имя (короткое, без пути) в `ServiceAuth:MethodPolicies` в `appsettings.json` владельца (`BillingService`, `ContentService`), иначе он останется открытым для любого s2s-вызывающего.
6. Клиенту — метод в обёртке (`*Provider`), а не прямой вызов `*Client` из страниц/хендлеров.

**Не класть сюда бизнес-логику** — только контракты/DTO. Общие хелперы (Money, TokenUsage, ошибки, интерсепторы) — в BuildingBlocks.
