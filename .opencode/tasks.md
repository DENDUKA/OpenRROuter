# Tasks

> Список задач для агентов. Статус управляется командами
> `/add-task` (добавить) и `/start-task` (взять в работу в отдельном агенте).
> Файл коммитится в git — это общее состояние бэклога, а не локальный черновик.

<!-- Формат одной задачи (легенду не удалять).
Идентификатор — <PREFIX>-NNN. Каждая крупная задача берёт свой префикс и свою нумерацию
с 001, чтобы несвязанные пачки не смешивались: T- — аудит репозитория, VS- — сессии визитов.
## <PREFIX>-NNN — <заголовок>
- status: todo | in_progress | done | blocked
- priority: low | medium | high
- created:  <ISO8601>
- started:  <ISO8601>    (заполняется при взятии в работу)
- completed:<ISO8601>    (заполняется при завершении)
- desc: <что сделать>
-->

> Источник: аудит всего репозитория от 2026-07-26.
> Полный отчёт с обоснованиями — `C:\Users\denis\.claude\plans\effervescent-orbiting-sparkle.md`.
> 65 задач: P0 (T-001..T-007) — критично, P1 (T-008..T-020) — высокий,
> P2 (T-021..T-053) — средний, P3 (T-054..T-065) — низкий.
> Рекомендуемый порядок: T-002+T-003 → T-001,T-004,T-010,T-029 → T-005,T-006 → T-007+T-008+T-018.

> Источник: схлопывание визитов в сессии (`/admin/sessions`), 2026-07-28.
> План — `C:\Users\denis\.claude\plans\admin-visitors-lucky-hickey.md` (там же DDL и SQL компактора).
> 10 задач: VS-001..VS-010.
> Порядок: VS-001 → VS-002 → VS-003 → VS-004 (критический путь); параллельно
> VS-005 → VS-006 → VS-007 и VS-008; VS-009 после VS-004; VS-010 последней.

---

## T-001 — Бонус за первое пополнение не выдаётся никогда
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-26T23:39:10+04:00
- completed: 2026-07-26T23:45:01+04:00
- desc: `BillingApplicationService.cs:777`. `ConfirmPaymentAtomicAsync` (`BillingRepository.cs:1277`) вставляет леджер с `isUserPayment: true` ДО того, как `PaymentWebhookProcessor.cs:139` вызывает `GrantFirstTopUpBonusAsync`. Внутри `HasUserHadPaidTopUpAsync` (`BillingRepository.cs:1024`) считает все строки `is_user_payment = true`, включая только что созданную → всегда `true` → `"not_first_paid_top_up"`. Промо мертво в проде. Фикс: передать `paymentId` и добавить `and idempotency_key <> 'payment:' || @PaymentId`, либо начислять бонус внутри транзакции подтверждения. Обязательны unit-тесты: бонус выдаётся на первом платеже и не выдаётся на втором.

## T-002 — RPC, печатающие деньги, не защищены s2s-аутентификацией
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started:  2026-07-27T11:39:59+04:00
- completed:2026-07-27T11:55:52+04:00
- desc: `BillingService/Infrastructure/Auth/ServiceAuthInterceptor.cs:18-33`. В `ProtectedMethods` нет `GrantStartBalance`, `GrantFirstTopUpBonus` (обе пишут положительные суммы в леджер) и `SetPlatformSetting` (`billing.proto:44-47`) — интерцептор для них делает `return`. Любой с доступом к порту 5002 печатает баланс в цикле. Фикс: добавить все три в `ProtectedMethods` и в `MethodPolicies` во всех окружениях (`appsettings.json:29-43`, compose, kustomize).

## T-003 — Рабочий s2s-токен закоммичен как дефолт
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started:  2026-07-27T11:39:59+04:00
- completed:2026-07-27T11:55:52+04:00
- desc: `BillingService/appsettings.json:26` — `"apigateway": "test-s2s-token-12345"`, активен везде, где env `ServiceAuth__Tokens__apigateway` не задан. Вместе с T-002 — полный обход s2s-защиты Billing. Фикс: пустая строка по умолчанию + fail-fast при старте вне Development, если токен не задан (по образцу `PaymentStartupGuard`). Делать одним коммитом с T-002.

## T-004 — Промо-настройки сохраняются в культуре запроса → сумма искажается в 100 раз
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:10:00+04:00
- desc: `WebApp/Pages/Admin/Index.cshtml.cs:84, 88`. `ToString("F2")` без `InvariantCulture`; `UseRequestLocalization` ставит `CurrentCulture = ru` → сохраняется `"1,00"`. Читается через `GetDecimalSettingAsync:124` и `BillingProvider.cs:877` с `NumberStyles.Number, InvariantCulture` → запятая как разделитель тысяч → `1.00` становится `100`, бонус `20%` → `2000%`. Фикс: `InvariantCulture` в оба `ToString("F2")`; на чтении `NumberStyles.Float` без `AllowThousands`. ВАЖНО: проверить прод — не испорчены ли уже значения в `billing.platform_settings`.

## T-005 — Статус usage_missing нарушает CHECK-констрейнт, usage-события теряются
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T12:19:43Z
- desc: `ChatCompletionHandler.cs:126, 308` и `TextToSpeechHandler.cs:116` пишут `status = "usage_missing"`, а `db/migrations/gateway/002_usage_outbox_status_check.sql:32` разрешает только `'pending','processing','completed','failed'`. INSERT падает, исключение глотается в `RecordUsageAsync` (`ChatCompletionHandler.cs:415`), клиент получает 200, событие исчезает. Статус уже знают `BillingApplicationService.cs:800` и `Admin/Requests.cshtml.cs:178` — рассинхрон схемы и кода. Фикс: новая миграция `db/migrations/gateway/003_*.sql`, добавляющая `'usage_missing'` в констрейнт. Существующие миграции не редактировать.

## T-006 — Rate-limit платёжного вебхука обходится подделкой X-Forwarded-For
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:03:14+04:00
- desc: `WebApp/Program.cs:366-371` — `UseForwardedHeaders` с пустыми `KnownNetworks`/`KnownProxies`, поэтому `Connection.RemoteIpAddress` переписывается клиентским заголовком. Лимитер (`Program.cs:351`) партиционируется ровно по нему → атакующий получает по 60 запросов в минуту на каждое выдуманное значение на анонимном `POST /webhook/{provider}`. Фикс: прописать подсеть Caddy в `KnownNetworks`/`KnownProxies` либо `ForwardLimit = 1`.

## T-007 — Гонка на in_flight_requests: read-modify-write всей строки ноды
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:15:12+04:00
- desc: `ControlPlane/Application/Services/RouterControlPlaneService.cs:141-142, 160-163`. `node.Lease()` инкрементит объект в памяти, а `UpsertNodeAsync` перезаписывает ВСЕ колонки строки. Два параллельных `SelectRouterNode` теряют инкремент; параллельный `ApplyHealth`/`UpdateNode` затирает `in_flight`, `enabled`, `status`, `control_token` устаревшими значениями — задизейбленная нода «воскресает». Корень CONTROL-004. Фикс: атомарный SQL `update ... set in_flight_requests = in_flight_requests + 1 where id = @Id returning ...`; health-путь — точечный `update` только health-колонок. Делать вместе с T-008 и T-018.

---

## T-008 — ReleaseExpiredLeasesAsync не атомарна и декрементит на 1 за ноду
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:15:12+04:00
- desc: `RouterControlPlaneService.cs:180-188` + `ControlPlaneRepository.cs:138-152`. DELETE и UPDATE на разных соединениях без транзакции, вызывается на горячем пути `SelectRouterNodeAsync:100` с токеном клиентского запроса. Клиент отвалился между вызовами — лизы удалены, счётчик не уменьшен. Плюс `Distinct()` + `in_flight_requests - 1`: 10 протухших лизов на ноде уменьшают счётчик на 1. Фикс: DELETE+UPDATE одним CTE с `greatest(in_flight - c, 0)`; в фоне использовать `stoppingToken`, а не токен gRPC-вызова.

## T-009 — Сломанная keyset-пагинация в AuthRepository: пропуск и дублирование строк
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:27:34+04:00
- desc: `AuthRepository.cs:109` и `:186`. Курсор фильтрует `where id > @AfterUserId` (по возрастанию), а сортировка — `order by created_at_utc desc, id desc`. Ключ курсора не совпадает с ключом сортировки → вторая страница возвращает произвольное подмножество. То же в `ListApiKeysAsync`. Фикс: `(created_at_utc, id) < (@AfterCreatedAt, @AfterId)` — как уже правильно сделано в `BillingRepository.ListUsageAsync:427`. Нужен тест на две страницы без пропусков и дублей.

## T-010 — Recovery-воркер игнорирует Payments:AutoReverseChargeback
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:31:14+04:00
- desc: `BillingService/Infrastructure/Workers/PaymentEventRecoveryWorker.cs:28` — `autoReverseChargeback` не передаётся в `AcquireUnprocessedEventsAsync` и берёт дефолт `false`, тогда как прямой путь (`BillingApplicationService.cs:616`) читает флаг из опций. Chargeback, попавший в recovery, не сторнируется: платёж уходит в `ChargedBack`, деньги остаются у пользователя. Фикс: заинжектить `IOptions<PaymentOptions>` и прокинуть флаг. Нужен тест на recovery-путь.

## T-011 — /admin/topups: фильтр с 0 совпадений показывает все чужие пополнения
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:37:12+04:00
- desc: `WebApp/Pages/Admin/TopUps.cshtml.cs:56-75` + `BillingProvider.cs:254`. `MatchUsers` при непустом запросе и нулевом числе совпадений возвращает пустой список, а `if (userIds is { Count: > 0 })` трактует пустой как «фильтра нет» → админ получает полный список чужих финансовых операций. Фикс: различать «фильтр не задан» (`null`) и «фильтр не дал совпадений» (пустой результат без похода в Billing).

## T-012 — Коды сброса пароля и верификации email хешируются голым SHA-256
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T14:30:00+04:00
- completed: 2026-07-27T15:10:00+04:00
- desc: `AuthService/Infrastructure/Security/Sha256SecretHasher.cs:17`. Один хешер применяется и к API-ключам (36 байт энтропии — ок), и к 6-значным кодам из `EmailVerificationService.cs:77`. Пространство 10^6 — при утечке дампа `auth.password_resets` радужная таблица считается за миллисекунды → захват аккаунтов через сброс пароля. Фикс: PBKDF2 для кодов (`Pbkdf2PasswordHasher` уже есть) либо HMAC с серверным ключом / привязка `user_id` как соли.

## T-013 — XSS в описаниях моделей: markdown-пайплайн пропускает сырой HTML
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:10:00+04:00
- desc: `WebApp/Services/Markup/MarkdownRenderer.cs:14-16, 48`; вывод — `Pages/ModelDetails.cshtml:70`. У `Pipeline` нет `DisableHtml()` (в отличие от соседнего `StrictPipeline:23`), защищает только regex-скраббер, требующий пробела перед атрибутом — `<div/onclick=alert(1)>` и entity-кодированный `javascript:` проходят. Источник — описания из OpenRouter (сторонний фид), рендерятся анонимным посетителям. Фикс: `DisableHtml()` и в `Render()`, либо настоящий санитайзер (HtmlSanitizer/AngleSharp).

## T-014 — SVG-логотип провайдера отдаётся без nosniff, content-type берётся от клиента
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:15:00+04:00
- desc: `WebApp/Controllers/PublicProviderLogoController.cs:21-24` + `Pages/Admin/Providers.cshtml.cs:101-117`. При загрузке валидируется только клиентский `ContentType`, `image/svg+xml` разрешён, содержимое не проверяется. На отдаче нет `X-Content-Type-Options: nosniff` — в отличие от соседних `PublicToolLogoController.cs:23` и `PublicToolImageController.cs:27`. SVG со скриптом выполняется в origin приложения. Фикс: `nosniff` + `Content-Disposition: inline`, тип определять по magic bytes.

## T-015 — Двойная запись ответа при исчерпании нод (non-stream путь)
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T12:00:00+04:00
- completed: 2026-07-27T12:10:00+04:00
- desc: `ChatCompletionHandler.cs:55-65`, `ImageGenerationHandler.cs:50-59`, `TextToSpeechHandler.cs:67-76`. `AuthorizeAndAcquireLeaseAsync` при `lease == null` уже сам записал 503 (`GatewayPipeline.cs:85`), поток отфлашен → `HasStarted = true`. Присвоение `StatusCode` кидает `InvalidOperationException`, ловится внешним catch. Клиент получает generic 503 вместо реальной ошибки ноды + ложный warning в логах на каждом failover. Стриминговый путь этого лишён (`PassThroughErrorAsync` проверяет `HasStarted`). Фикс: проверка `HasStarted` перед записью либо флаг `suppressErrorWrite`.

## T-016 — Тело запроса пересериализуется 3-5 раз за попытку ретрая
- status: done
- completed: 2026-07-27T23:59:00+04:00
- priority: high
- created: 2026-07-26T19:30:00Z
- desc: `ChatCompletionHandler.cs:37, 69, 89, 228, 248-252`. `ReadBodyAsync` читает тело в `string` (LOH на длинном контексте), далее на КАЖДОЙ итерации ретрая `SseRequestPatcher.PatchModel`, `ReasoningRequestPatcher.InjectEffort`, `GeminiSchemaPatcher`, `InjectStreamOptions` — каждый делает свой `JsonNode.Parse` + `ToJsonString`. До 5 полных парсов мегабайтного JSON на запрос, ×5 при `MaxRouterAttempts`. Фикс: один `JsonNode` в `ReadBodyAsync`, все патчи к одному DOM, одна сериализация.

## T-017 — Полная реавторизация Auth+Billing на каждой попытке ретрая
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:15:00+04:00
- desc: `ChatCompletionHandler.cs:51, 216` → `GatewayPipeline.cs:49, 57`. `AuthorizeAndAcquireLeaseAsync` вызывается внутри `while(true)` и всегда делает `AuthenticateAsync` + `CheckBalanceAndModelAsync`, хотя от ноды зависит только `SelectRouterNodeAsync`. При 5 попытках — 10 лишних gRPC round-trip'ов. Фикс: разделить на `AuthorizeAsync` (один раз до цикла) и `AcquireLeaseAsync(excluded)` (в цикле). Это мемоизация внутри одного запроса, НЕ кеш между запросами.

## T-018 — SelectRouterNode читает всю таблицу нод на каждый чат-запрос
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- completed: 2026-07-27T18:15:12+04:00
- desc: `RouterControlPlaneService.cs:100-133`. На каждый запрос: DELETE протухших лизов + `ListNodesAsync(1000)` + материализация в `List` + цепочка `Where/OrderBy` + `UpsertNodeAsync` + `AddLeaseAsync` — 4 round-trip'а в Postgres, плюс decimal-деление в сортировке. Фикс: выбор ноды одним SQL с `order by ... limit 1 for update skip locked` и инкрементом в том же запросе (закрывает и T-007); метаданные нод — in-memory снапшот по паттерну `CollectorSnapshotStore`, обновляемый воркером раз в 5-10 с. ВАЖНО: в снапшот НЕЛЬЗЯ класть `in_flight_requests` — это мутабельный счётчик, иначе кеш законсервирует гонку T-007.

## T-019 — Лейбл client_ip в Prometheus-счётчике: неограниченная кардинальность
- status: done
- priority: high
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T12:00:00+04:00
- completed: 2026-07-27T12:05:00+04:00
- desc: `ApiGateway/Infrastructure/Startup/VisitorMetricsMiddleware.cs:59-67`. `orr_http_requests_total` содержит `client_ip` и пишется на каждый запрос публичного эндпоинта; каждый новый IP создаёт временную серию, живущую в памяти процесса до перезапуска (`Counter<long>` в OTel не вытесняется) → OOM гейтвея и взорванный Prometheus под ботовым трафиком. Прямо противоречит правилу из корневого CLAUDE.md про кардинальность `LimitMetrics`. Фикс: убрать `client_ip`, оставить `method/path/status`.

## T-020 — Рассылка блокирует HTTP-запрос навсегда
- status: done
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:15:00+04:00
- priority: high
- created: 2026-07-26T19:30:00Z
- desc: `WebApp/Email/EmailBroadcastService.cs:43`, `EmailDispatchQueue.cs:14-19`, `Pages/Admin/Broadcast.cshtml.cs:42`. `EnqueueBatchAsync` пишет по одному в `Channel.CreateBounded(10_000, FullMode = Wait)` и вызывается без токена отмены (`ct = default`). Воркер дренит ~1 msg/s — при >10 000 адресатов или упавшем транспорте POST виснет на часы и не отменяется при закрытии вкладки. Всё в памяти теряется при рестарте. Фикс: прокинуть `HttpContext.RequestAborted`, постановку батча — в фон/outbox-таблицу.

---

## T-021 — count(*) по всей ledger_entries на каждой странице /admin/topups
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-27T23:59:00+04:00
- desc: `BillingRepository.cs:49`. Индекса по `type` нет, таблица растёт на строку с каждого API-запроса → seq scan + offset-пагинация (`:56`). Фикс: миграция `create index ... on billing.ledger_entries (type, created_at_utc desc)` + заменить точный `count(*)` на keyset-курсор или кешируемое приближение.

## T-022 — Нет составного индекса под фактический ORDER BY в ListUsageAsync
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:10:00+04:00
- desc: `BillingRepository.cs:445-446` — `where user_id = @UserId and (created_at_utc, request_id) < (...) order by created_at_utc desc, request_id desc`. В миграциях есть только `(user_id)` (001) и `(created_at_utc)` (005) по отдельности → Postgres берёт все записи пользователя и сортирует целиком. Для `activity_by_token` индекс завели (миграция 021), для `usage_records` забыли. Фикс: миграция `create index ... on billing.usage_records (user_id, created_at_utc desc, request_id desc)`.

## T-023 — Двойной count(*) + data на каждый листинг активности
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T12:00:00+04:00
- completed: 2026-07-27T12:15:00+04:00
- desc: `BillingRepository.cs:659-660`. `TotalCount` пересчитывается при каждом пролистывании кабинета, хотя курсорная пагинация (`hasMore`) в том же методе уже есть. Заодно это единственное место в файле без `ConfigureAwait(false)`. Фикс: считать `TotalCount` только на первой странице либо отдать `count(*) over ()` в основном запросе.

## T-024 — count(*) вместо exists в проверке «был ли платный топап»
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T12:00:00Z
- completed: 2026-07-28T12:10:00Z
- desc: `BillingRepository.cs:1028-1035` — `select count(*)` и `return count > 0`. Дёргается на каждом рендере `/` (`Index.cshtml.cs:62`) и `/workspace/credits` (`Credits.cshtml.cs:169`). Фикс: `select exists (select 1 ... limit 1)` — частичный индекс `ix_billing_ledger_entries_user_is_user_payment` (миграция 023) тогда даст index-only scan за один tuple.

## T-025 — ListPublicModels выгружает каталог моделей дважды
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T15:45:00+04:00
- completed: 2026-07-28T15:55:00+04:00
- desc: `GrpcBillingService.cs:55-57` — два независимых запроса с `ModelPriceSelectSql` (25 колонок + `left join model_providers`) и двумя соединениями, при том что результат `ListAllModelsAsync` — надмножество `ListPublicModelsAsync`. Фикс: строить ответ из одного `ListAllModelsAsync`, отфильтровав `Enabled`. Дополнительно — кеш каталога с TTL 60 с + инвалидация в `UpsertModelPrice`.

## T-026 — GetPromotionSettings: четыре последовательных запроса за четырьмя строками
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T17:00:00+04:00
- completed: 2026-07-28T17:15:00+04:00
- desc: `GrpcBillingService.cs:1101-1104` — четыре `GetPlatformSettingAsync`, каждый со своим соединением; дёргается на рендере лендинга и страницы пополнения. Здесь же протекающий фасад: gRPC-слой лезет в репозиторий в обход `BillingApplicationService`. Фикс: один запрос `where key = any(@Keys)` + метод `GetPlatformSettingsAsync(IReadOnlyCollection<string>)` в фасаде. Плюс кеш с инвалидацией в `SetPlatformSetting` (тот же процесс) и TTL 60 с как страховка.

## T-027 — Три round-trip'а к БД на каждую валидацию API-ключа (горячий путь шлюза)
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T18:00:00+04:00
- completed: 2026-07-28T18:15:00+04:00
- desc: `AuthApiKeyValidator.cs:18-23` — `FindActiveApiKeyBySecretHashAsync` → `MarkApiKeyUsedAsync` → `GetUserAsync`, три отдельных `CreateConnection()`. Первый запрос (`AuthRepository.cs:292-296`) уже делает `inner join auth.users u`, но `u.role` не попал в select-лист, из-за чего нужен третий запрос. Фикс: добавить `u.role` в select первого запроса — третий round-trip исчезает БЕЗ кеша. Кеш валидации здесь НЕ применять: отзыв ключа происходит в AuthService, а кешировал бы ApiGateway — явная инвалидация невозможна, отозванный ключ жил бы ещё TTL секунд.

## T-028 — Гонка на балансе: проверка и списание не связаны
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:35:00+04:00
- desc: `BillingApplicationService.cs:107` (`GetBalanceAsync`) → `:295` (`RecordUsageWithTransactionAsync`). `CheckBalanceAndModelAsync` читает `sum(amount)` (`BillingRepository.cs:114`) без блокировки и проверяет только `<= 0`, списание — отдельной транзакцией минутами позже. N параллельных запросов при балансе $0.000001 все проходят. Плюс полная агрегация леджера пользователя на каждый запрос, растущая линейно. Фикс: денормализовать баланс в `billing.user_balances` с `update ... returning` внутри транзакции списания. ВНИМАНИЕ: баланс кешировать НЕЛЬЗЯ ни при каких условиях.

## T-029 — AcquireStalePaymentsAsync теряет bonus_percent/bonus_usd
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started:  2026-07-27T11:10:53+04:00
- completed:2026-07-27T11:26:32+04:00
- desc: `BillingRepository.cs:1198-1205`. Select-лист CTE `updated` перечислен вручную и обрывается на `canceled_at_utc` — `bonus_percent`/`bonus_usd` (миграция 016) отсутствуют, хотя `PaymentEntity` их имеет (`:31-32`). Dapper молча оставляет `null`, и `GrantFirstTopUpBonusAsync:770` бракует платёж с `"no_bonus_snapshot"` — реконсиляция теряет бонус даже после починки T-001. Фикс: заменить ручной список на `PaymentSelectSql`-совместимый.

## T-030 — Расхождение порога длины пароля: 8 в фасаде против 12 в хешере
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:15:00+04:00
- desc: `AuthAccountApplicationService.cs:53` (`MinPasswordLength = 8`) vs `Pbkdf2PasswordHasher.cs:56` (бросает `ArgumentException` при `< 12`). В `RegisterUserAsync:77`, `ResetPasswordAsync:219`, `ChangePasswordAsync:254` исключение не перехвачено, `GrpcAuthService.RegisterUser` тоже не ловит → клиент получает `Unknown`/500 вместо `weak_password`. Фикс: свести порог в одно место (рекомендуется 12).

## T-031 — Sha256SecretHasher.Verify падает FormatException на некорректном хеше
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T16:00:00+04:00
- completed: 2026-07-27T16:05:00+04:00
- desc: `Sha256SecretHasher.cs:31` — `Convert.FromBase64String` на обоих аргументах; проверка на `:23` отсекает только пустую строку. Любая не-base64 строка в `code_hash`/`token_hash` даёт `FormatException`, вылетающий из `EmailVerificationService.Check` наверх вместо безопасного отказа. Фикс: `Convert.TryFromBase64String` и `return false` при неудаче.

## T-032 — Внешний логин выдаёт сессию аккаунту в статусе PendingVerification
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:30:00+04:00
- desc: `ExternalLoginService.cs:38-51`. Ветка линковки по email проверяет только `Status == UserStatus.Disabled`. Аккаунт, зарегистрированный через `/signup` и не подтвердивший email, получает полноценную сессию через Google/Yandex и навсегда остаётся `PendingVerification` (`Activate()` не вызывается) — после чего `LoginService.cs:41-44` отбивает его же вход по паролю, а `RequestPasswordResetAsync:170` отказывает в сбросе. Фикс: при линковке по email активировать аккаунт, раз провайдер подтвердил владение почтой.

## T-033 — Бонус первого пополнения считается без округления до 6 знаков
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started:  2026-07-27T11:11:02+04:00
- completed:2026-07-27T11:32:53+04:00
- desc: `BillingApplicationService.cs:781` — `payment.CreditedUsd.Amount * payment.BonusPercent.Value / 100m` без округления. Значение уходит в `numeric(18,6)` леджера и в `numeric(12,6)` `payments.bonus_usd`, округление делает Postgres, а `UpdatePaymentBonusAsync` пишет неокруглённое → колонки расходятся в 6-м знаке. Заодно в проекте намешаны `AwayFromZero` (`CreatePaymentAsync:553`) и `ToEven` (`UsageCostCalculator.cs:27`). Фикс: `decimal.Round(..., 6, ...)` + единая политика округления денег.

## T-034 — TOCTOU на лимите числа API-ключей
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T18:00:00+04:00
- completed: 2026-07-28T18:30:00+04:00
- desc: `AuthAccountApplicationService.cs:274-281` — `CountApiKeysAsync` → проверка `>= user.MaxApiKeys` → `AddApiKeyAsync` тремя отдельными соединениями без транзакции; параллельные запросы обходят лимит. В `db/migrations/auth/001_initial.sql` ограничения нет. Фикс: `insert ... select ... where (select count(*) ...) < @Max` одним statement, 0 затронутых строк = `ApiKeyLimitReached`.

## T-035 — SSE-парсер требует ровно "data: " с пробелом
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-28T00:10:00+04:00
- desc: `RouterStreamSession.cs:40-43` — `StartsWith("data: ")` + `Substring(6)`, тогда как `TokenUsageExtractor.cs:63-66` в том же репозитории обрабатывает `"data:"` + `TrimStart()`. Спецификация SSE допускает `data:{...}` без пробела → апстрим, отдающий такой формат, для стрима вернёт клиенту ПУСТОЙ поток, при этом usage из non-stream-ответа того же апстрима извлечётся нормально. Фикс: матчить `"data:"` + `TrimStart()`, привести к поведению `TokenUsageExtractor`.

## T-036 — Image/TTS не ретраят обрыв соединения: 500 вместо failover
- status: done
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:10:00+04:00
- priority: medium
- created: 2026-07-26T19:30:00Z
- desc: `ImageGenerationHandler.cs:105-109`, `TextToSpeechHandler.cs:137-141` — только `catch { release("failed"); throw; }`, тогда как `ChatCompletionHandler.cs:146` ловит `HttpRequestException`/`IOException` и уходит на другую ноду. Протухший pooled HTTP/2-коннект или краш ноды дают клиенту 500 без единой попытки failover. Фикс: единая обработка исключений во всех хендлерах (см. T-056 про вынос retry-цикла).

## T-037 — TTS биллит UTF-16 code units вместо символов
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T00:00:00+04:00
- completed: 2026-07-28T00:10:00+04:00
- desc: `TextToSpeechHandler.cs:113-115` — комментарий обещает «количество символов Unicode», а `input.Length` считает UTF-16 code units: эмодзи и любой символ вне BMP идёт за 2. Расходится и с комментарием, и с биллингом OpenAI. Фикс: `new StringInfo(input).LengthInTextElements` или `input.EnumerateRunes().Count()`; поправить комментарий.

## T-038 — Фильтры дат в /admin/visitors зависят от культуры и таймзоны сервера
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T12:00:00+04:00
- completed: 2026-07-27T12:10:00+04:00
- desc: `Visitors.cshtml.cs:123-124` — `DateTimeOffset.TryParse` без `InvariantCulture` и без `DateTimeStyles.AssumeUniversal | AdjustToUniversal`. Документация страницы обещает UTC `yyyy-MM-dd`, а строка разбирается в культуре запроса и offset получается локальным для сервера → окно в `SiteVisitReader.ApplyTimeWindow` смещено. Фикс: `TryParseExact("yyyy-MM-dd", InvariantCulture, AssumeUniversal | AdjustToUniversal)`.

## T-039 — GeminiSchemaPatcher рекурсивно обходит весь запрос, включая messages
- status: done
- started: 2026-07-28T20:00:00+04:00
- completed: 2026-07-28T20:15:00+04:00
- priority: medium
- created: 2026-07-26T19:30:00Z
- desc: `GeminiSchemaPatcher.cs:46-51`. `CollectDefinitions(request)` идёт с корня по всему JSON (вся история диалога), затем `SanitizeSchemaRootsByName` делает второй полный обход, `SanitizeSchemaBranch` копирует словарь и `DeepClone()`-ит определения на каждую из трёх веток, `RemoveDefinitionBags`/`RemoveRemainingRefs` — ещё два обхода с `obj.ToList()` на каждом узле. Для gemini-моделей с длинным контекстом — секунды CPU и мегабайты мусора на запрос. Фикс: собирать `$defs` только внутри `tools`/`functions`/`response_format`; объединить обходы в один проход.

## T-040 — SSE: полный JSON-парсинг и аллокации на каждый чанк
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-29T12:00:00Z
- completed: 2026-07-29T12:30:00Z
- desc: `RouterStreamSession.cs:43, 52-60`. На каждом чанке: `Substring(6)`, `RouterUsageExtractor.TryExtract` → `JsonDocument.Parse`, и поскольку у delta-чанков токенов нет — фолбэк в `TokenUsageExtractor.TryExtractStreamingSse` с `new StringReader(...)` (`RouterUsageExtractor.cs:10-12`), затем конкатенация строк + `Encoding.UTF8.GetBytes` + `FlushAsync`. Ответ в 2000 чанков = ~4000 парсов JSON. Фикс: проверять `data.Contains("\"usage\"", Ordinal)` до парсинга; не звать SSE-фолбэк для одиночного чанка; писать через `PipeWriter` без конкатенации.

## T-041 — YARP-прокси буферизует запрос и ответ целиком, тело конвертируется 4 раза
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-29T00:00:00+04:00
- completed: 2026-07-29T00:15:00+04:00
- desc: `YarpRouterProxyProvider.cs:69-70, 73, 84-86` (то же в `:134`, `:177`, `:199`). Цепочка `string` → `byte[]` → `MemoryStream` для запроса и `MemoryStream` → `string` → снова UTF-8 для ответа; для TTS добавляется `responseBody.ToArray()` (`:199`) — вторая копия аудиобуфера. Фикс: `responseBody.CopyToAsync(context.Response.Body)` без промежуточной строки; `RouterProxyResponse.Body` для бинарных ответов не заполнять.

## T-042 — /v1/models публичный, без кеша и без rate-limit
- status: done
- completed: 2026-07-29T15:30:00+04:00
- priority: medium
- created: 2026-07-26T19:30:00Z
- desc: `ModelsHandler.cs:28-109`, регистрация без `RequireRateLimiting` — `ApiGatewayExtensions.cs:190-191`. Эндпоинт без Bearer, на каждый вызов делает gRPC `ListPublicModelsAsync` в Billing и строит на каждую модель `List<string>` + два `Dictionary<string, object?>` + анонимный тип, затем сериализует рефлексией. Готовый усилитель нагрузки на Billing с анонимного трафика. Фикс: кешировать СЕРИАЛИЗОВАННЫЙ ответ (TTL 30-60 с, только TTL — межсервисная инвалидация невозможна) + отдельная мягкая rate-limit-политика.

## T-043 — Квадратичный LINQ в цикле опроса нод
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:30:00+04:00
- desc: `CollectorService/Application/Collection/NodePollingWorker.cs:57` и `:105-110`. `catalogResults.FirstOrDefault(c => c.NodeId == measurement.NodeId)` — O(N) внутри O(M); глубже `foreach (node) foreach (observation) { flatMeasurements.Where(...).ToList(); }` с аллокацией списка на каждой итерации. При 10 нодах × 500 моделей × 50 квот — сотни тысяч проходов каждую минуту. Фикс: построить `Dictionary<nodeId, ...>` и `ILookup<(nodeId, connectionId), ...>` один раз до циклов.

## T-044 — Health-чек всех нод последовательный, Task.Delay вне try
- status: done
- started: 2026-07-29T12:00:00Z
- completed: 2026-07-29T12:10:00Z
- priority: medium
- created: 2026-07-26T19:30:00Z
- desc: `ControlPlane/Infrastructure/Workers/RouterHealthCheckWorker.cs:24-34` и `:54`. `foreach` с `await` внутри: одна нода на таймауте (дефолт 5 с) задерживает все последующие — 20 недоступных нод дают раунд 100 с при интервале 30 с. Плюс `await Task.Delay(..., stoppingToken)` вынесен ЗА `try` (в отличие от `CapabilityReplicaRefresher.cs:64-71` и `NodePollingWorker.cs:258-265`) → на shutdown `TaskCanceledException` уходит из `ExecuteAsync`. Фикс: `Parallel.ForEachAsync`/`SemaphoreSlim`, `Task.Delay` внутрь `try/catch (OperationCanceledException)`.

## T-045 — /admin/users: неограниченный веер gRPC-вызовов, нет пагинации
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:15:00+04:00
- desc: `Pages/Admin/Users.cshtml.cs:87-103` — `ListUsersAsync()` тянет весь список без страниц, затем `Users.Select(async user => GetBalanceAsync(user.Id))` + `Task.WhenAll` даёт по одному gRPC-вызову на пользователя одновременно. На 5-10 тыс. аккаунтов — залп в Billing с одного HTTP-запроса. Тот же беспагинационный `ListUsersAsync` в `TopUps.cshtml.cs:34`, `Visitors.cshtml.cs:135`, `EmailBroadcastService.cs:30`. Фикс: пагинация + батч-RPC `GetBalances(userIds[])`; как минимум `SemaphoreSlim`. Кешировать НЕ надо — админ смотрит то, что правит.

## T-046 — Обогащение каталога: последовательный N+1 внутри HTTP-запроса
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:00:00+04:00
- completed: 2026-07-28T23:15:00+04:00
- desc: `Pages/Admin/Models.cshtml.cs:285-362` — `foreach (var model in models)` с `await _billingClient.UpsertModelAsync(...)` внутри (`:360`). На каталоге в 200+ моделей это сотни последовательных gRPC-вызовов в одном POST; запрос упирается в таймаут прокси и падает посередине, оставляя каталог частично обогащённым. Любое исключение рвёт весь батч и уходит в `catch` на `:373`. Фикс: батч-RPC `UpsertModels(...)` либо вынос в фоновую задачу со статусом.

## T-047 — ListModelProvidersAsync тянет байты логотипов, которые не используются
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T19:00:00+04:00
- completed: 2026-07-28T19:15:00+04:00
- desc: `BillingProvider.cs:792` безусловно материализует `p.LogoBytes.ToByteArray()`, а все три потребителя (`Providers.cshtml.cs:18`, `Models.cshtml.cs:174`, `Index.cshtml.cs:34`) используют байты только для проверки `is { Length: > 0 }` — саму картинку браузер грузит отдельным запросом на `/providers/logo/{id}`. Десятки логотипов по ~256 KiB гоняются по gRPC и выбрасываются на каждый рендер `/`, `/models`, `/providers`. Фикс: флаг `include_logo=false` или поле `has_logo` в `ListProvidersRequest`. Это НЕ решается кешем — кешировать 256 KiB, чтобы их выбросить, лечит симптом.

## T-048 — Кеш провайдеров держит логотипы час, IMemoryCache без SizeLimit
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T12:00:00+04:00
- completed: 2026-07-28T12:30:00+04:00
- desc: `Pages/Models.cshtml.cs:169-176`, `Program.cs:65`. `GetCachedProvidersAsync` кладёт в кеш весь список ВМЕСТЕ с `LogoBytes` на час, а `AddMemoryCache()` вызван без `SizeLimit` и записи создаются без `entry.Size` — ни вытеснения по объёму, ни потолка. Плюс `GetOrCreateAsync` не защищён от stampede: на холодном кеше все параллельные запросы выполняют фабрику одновременно. Фикс: кешировать проекцию без байтов, задать `SizeLimit` + `entry.Size`, обернуть фабрику в `SemaphoreSlim`/`Lazy<Task<T>>`. Предпосылка для остальных кеш-задач (T-025, T-026, T-042, T-049, T-052).

## T-049 — Один gRPC round-trip на каждый логотип, без серверного кеша и ETag
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-29T18:00:00+04:00
- completed: 2026-07-29T18:05:00+04:00
- desc: `PublicProviderLogoController.cs:14`, `PublicToolLogoController.cs:18` — `GetLogo` на каждый запрос делает `GetModelProviderAsync(id)` в Billing. На `/models` с 20 провайдерами холодный посетитель порождает 20 отдельных gRPC-вызовов. `Cache-Control: public, max-age=604800` спасает только повторные визиты; `ETag`/`Last-Modified` не выставляются → после протухания браузер перекачивает целиком вместо 304. Фикс: кеш байтов в `IMemoryCache` (TTL 24 ч) + `ETag` из `UpdatedAtUtc` (`?v=` в URL уже есть — ключ готов).

## T-050 — Лендинг /: пять последовательных удалённых вызовов и три пустых catch
- status: done
- completed: 2026-07-30T12:15:00+04:00
- priority: medium
- created: 2026-07-26T19:30:00Z
- desc: `Pages/Index.cshtml.cs:30-69` — `ListModelProvidersAsync` → `GetPromotionSettingsAsync` → `GetBalanceAsync` → `ListApiKeysAsync` → `HasUserHadPaidTopUpAsync` строго друг за другом, хотя первые две пары независимы. Кеша нет (в отличие от `/models`). Все три `catch` пустые, без логирования (`:36`, `:46`, `:65`) → деградация Billing на главной невидима в Seq. Фикс: `Task.WhenAll` для независимых вызовов, кеш справочников (закрывается T-026 и T-048), `LogWarning` в каждом catch.

## T-051 — /admin/models/sync: последовательные пробы мёртвых нод по 20 с каждая
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T15:00:00+04:00
- completed: 2026-07-28T15:10:00+04:00
- desc: `Pages/Admin/ModelsSync.cshtml.cs:199-209` — `foreach (var nodeId in UnreachableNodeIds)` с `await ProbeProvidersAsync` внутри, а named-клиент `RouterNode` имеет `Timeout = 20 s` (`Program.cs:264`). Пять мёртвых нод — до 100 секунд рендера страницы ровно тогда, когда админ туда и заходит. Фикс: `Task.WhenAll` + общий `CancellationTokenSource` с бюджетом 5-10 с.

## T-052 — ApiBaseUrlResolver ходит в Billing на каждый рендер страницы
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-30T12:00:00+04:00
- completed: 2026-07-30T12:15:00+04:00
- desc: `WebApp/Services/ApiBaseUrlResolver.cs:21` — `ResolveAsync` безусловно вызывает `GetPlatformSettingAsync(PublicApiBaseUrl)`, то есть gRPC round-trip на каждый показ `/models/{...}` и `/docs` ради значения, которое правится вручную раз в никогда. Фикс: кеш на 5-10 минут + явная инвалидация из `Admin/Index.OnPostSaveSettingsAsync` (писатель и читатель в одном процессе WebApp — инвалидация возможна).

## T-053 — Алерт-вебхук: лимит тела не работает без Content-Length, нет rate-limit
- status: done
- priority: medium
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T05:11:43+04:00
- completed: 2026-07-28T05:13:52+04:00
- desc: `WebApp/Api/AlertWebhookEndpoint.cs:62-71` — `if (http.Request.ContentLength is > MaxBodyBytes)`; при chunked-передаче `ContentLength` равен `null`, проверка пропускается и тело читается в память целиком. Соседний `PaymentWebhookEndpoint.cs:82` эту дыру закрывает через `LimitedStream`. Политики `RequireRateLimiting` на эндпоинте тоже нет. Фикс: обернуть тело в тот же `LimitedStream` (вынести в общий internal-класс) + rate-limit.

---

## T-054 — Нет ни одного security-заголовка на уровне приложения
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started:  2026-07-27T19:00:00+04:00
- completed:2026-07-27T19:15:00+04:00
- desc: `WebApp/Program.cs:377-408` — нет `UseHsts`, нет middleware с `Content-Security-Policy`, `X-Frame-Options`/`frame-ancestors`, `Referrer-Policy`, глобального `X-Content-Type-Options`. При cookie-аутентификации с `SameSite=Lax` админка открыта для clickjacking, отсутствие CSP снимает второй эшелон защиты для T-013. Фикс: middleware с базовым набором заголовков + `UseHsts()` вне Development.

## T-055 — lastFailedSession не диспозится на нескольких путях выхода
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started:  2026-07-27T18:45:00+04:00
- completed:2026-07-27T18:50:00+04:00
- desc: `ChatCompletionHandler.cs:238-245` (return при ошибке reasoning-патча), `:320-321` и `:357-358` (rethrow). Если первая нода вернула ошибку и сессия сохранена в `lastFailedSession`, а на следующей итерации сработал патч-error или исключение — `HttpResponseMessage` не освобождается, соединение висит в пуле `StreamingHttpClient` до GC. Фикс: `try/finally` вокруг `ExecuteStreamingAsync` или диспоз в `finally` внешнего `try`.

## T-056 — Дублирование retry-цикла в 4 хендлерах и расхождение MaxRouterAttempts
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T23:59:00+04:00
- completed: 2026-07-27T12:49:38+04:00
- desc: `ChatCompletionHandler.cs:49-185` и `:214-360`, `ImageGenerationHandler.cs:46-110`, `TextToSpeechHandler.cs:62-142` — четыре почти идентичные копии цикла «authorize → patch → send → release → exclude → retry» по ~100 строк. Лимит попыток в чате из конфига (`ChatCompletionHandler.cs:29`), в image/TTS захардкожен `const int MaxRouterAttempts = 5` → правка `GatewayRetry:MaxRouterAttempts` в проде затронет только чат. `HandleAsync` — 165 строк с тройной вложенностью. Фикс: `RouterRetryExecutor` с делегатами «патч тела» и «отправка», `IOptions<GatewayRetryOptions>` во все хендлеры. Закрывает и T-036.

## T-057 — Серверный ServiceAuthInterceptor продублирован в двух сервисах
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T22:50:00+04:00
- completed: 2026-07-27T13:14:00+04:00
- desc: `BillingService/Infrastructure/Auth/ServiceAuthInterceptor.cs` (169 строк) и `ContentService/Infrastructure/Auth/ServiceAuthInterceptor.cs` — 6846 vs 6871 байт, расходятся только списком `ProtectedMethods`. Корневой CLAUDE.md фиксирует это как известный факт. Фикс: вынести в BuildingBlocks/Grpc с конфигурируемым списком методов. Делать ПОСЛЕ T-002, чтобы не смешивать рефакторинг с фиксом безопасности.

## T-058 — EmitAnalytics побайтово повторён в четырёх местах
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T12:00:00+04:00
- completed: 2026-07-27T12:30:00+04:00
- desc: `BillingApplicationService.cs:688`, `AuthAccountApplicationService.cs:352`, `ExternalLoginService.cs:84` + мёртвая копия в `GoogleAuthService.cs:93`. Фикс: `IProductAnalytics.EmitSafe` в `BuildingBlocks/Analytics/`, мёртвую копию удалить.

## T-059 — BillingRepository.cs: 1681 строка
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-31T12:00:00+04:00
- completed: 2026-07-31T12:30:00+04:00
- desc: Разрезать по агрегатам: Ledger / Models / Usage / Payments. Делать ПОСЛЕ фиксов T-001, T-021..T-024, T-029 — иначе конфликты и потеря контекста при ревью.

## T-060 — GrpcBillingService.cs: 1129 строк, протекающий фасад
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T23:45:00+04:00
- completed: 2026-07-28T23:50:00+04:00
- desc: Местами лезет в `billing.Repository` напрямую в обход `BillingApplicationService` (пример — `:1101-1104` в `GetPromotionSettings`). Фикс: провести все обращения через фасад, разрезать файл по группам RPC. Пересекается с T-026.

## T-061 — BillingProvider.cs: 895 строк на шесть доменов, decimal.Parse без защиты
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-28T15:00:00+04:00
- completed: 2026-07-28T15:30:00+04:00
- desc: `WebApp/Services/Billing/BillingProvider.cs` обслуживает баланс, usage, каталог, теги, провайдеров, платежи и платформенные настройки; строки 556-776 отформатированы отступом в 3 пробела (след механического слияния). Обработка ошибок несогласованна: `ListPublicModelsAsync:295` защищает парсинг, а `ListModelsAsync:322-324` зовёт `decimal.Parse` напрямую — пустая цена роняет `FormatException` и всю админскую страницу моделей. Хелперы `ParseDecimal`/`ParseOptionalDecimal` уже есть на `:778-782`, просто не применены везде. Фикс: разбить на `BillingCatalogProvider`/`BillingUsageProvider`/`BillingPaymentsProvider`, применить общие парсеры.

## T-062 — InMemoryBillingRepository: 848 строк, GetAwaiter().GetResult() внутри lock
- status: done
- started: 2026-07-31T20:00:00+04:00
- completed: 2026-07-31T20:30:00+04:00
- priority: low
- created: 2026-07-26T19:30:00Z
- desc: `InMemoryBillingRepository.cs` дублирует контракт PG-репозитория целиком и делает `GetAwaiter().GetResult()` внутри `lock` на строках 746-748 (риск дедлока). Фикс: убрать синхронную блокировку, рассмотреть сокращение in-memory реализации после T-059.

## T-063 — Дублирование хелперов между page models каталога
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T15:00:00+04:00
- completed: 2026-07-27T15:30:00+04:00
- priority: low
- created: 2026-07-26T19:30:00Z
- desc: `ResolveCurrencyAsync` скопирован дословно в трёх page model'ах (`Models.cshtml.cs:137-158`, `ModelDetails.cshtml.cs:119-158`, `ProviderDetails.cshtml.cs:90-109`), `FormatContext` — в трёх, `GetInitials` — в двух, `ModalityIcon` с одинаковыми SVG-литералами — в `Admin/Models.cshtml.cs:655-662` и `ModelDetails.cshtml.cs`, и уже разошёлся размером иконок (14 vs 16). Фикс: `CatalogPageModelBase` (валюта + `FormatPrice`) + статический `CatalogFormatting`/`ModalityIcons` в `Services/Markup`.

## T-064 — /models/{provider}/{slug}: пустой catch и soft-404 с кодом 200
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T12:25:26+04:00
- completed: 2026-07-27T12:26:45+04:00
- desc: `ModelDetails.cshtml.cs:111-116` — пустой `catch {}` без логирования делает падение Billing неотличимым от «модели нет». При `ModelData == null` страница возвращает 200 OK и рисует заголовок, синтезированный из URL (`:54`) → любой `/models/foo/bar` даёт валидную страницу. Плюс два последовательных вызова `GetModelAsync` вместо одного. Фикс: логировать исключение, `NotFound()` при `ModelData == null`.

## T-065 — RemoveNodeAsync: комментарий обещает ON DELETE CASCADE, которого нет
- status: done
- priority: low
- created: 2026-07-26T19:30:00Z
- started: 2026-07-27T22:00:00+04:00
- completed: 2026-07-27T22:05:00+04:00
- desc: `ControlPlaneRepository.cs:43-48` — комментарий утверждает, что у лиз есть FK с каскадом, но в `db/migrations/control/001_initial.sql` у `control.router_leases.node_id` каскада нет. При живых лизах DELETE падает `PostgresException`, не ловится в `GrpcRouterControlPlaneService.cs:159` (там только `KeyNotFoundException`) — админка получает gRPC `Unknown`. `InMemoryControlPlaneRepository.cs:25-27` лизы удаляет, поэтому unit-тесты кейс не ловят. Фикс: миграция с `on delete cascade` либо явное удаление лиз в одной транзакции; комментарий привести в соответствие.

---

## VS-001 — Миграция: таблица analytics.visit_sessions
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-27T23:38:50+04:00
- completed: 2026-07-27T23:41:30+04:00
- desc: Новый файл `db/migrations/analytics/003_visit_sessions.sql` (`002_site_visits.sql` не трогать — применённые миграции иммутабельны). Катает BillingService автоматически, каталог сканируется целиком, правки в его `Program.cs` не нужны. DDL — в разделе «Технические детали» плана. Колонки: `id`, `visitor_hash`, `distinct_id`, `started_at_utc`, `ended_at_utc`, `duration_seconds`, `page_view_count`, `entry_path`, `exit_path`, `referrer_host`, `referrer_kind`, `utm_source/medium/campaign`, `device_class`, `is_authenticated`, `pages jsonb not null default '[]'::jsonb`, `environment`, `created_at_utc`; check-констрейнты `ended_at_utc >= started_at_utc` и `page_view_count > 0`. Индексы: **unique (visitor_hash, started_at_utc)** — он же арбитр `on conflict`; `(started_at_utc)`; partial `(started_at_utc) where device_class <> 'bot'`; partial `(distinct_id) where distinct_id is not null`. Новых индексов на `site_visits` НЕ добавлять: существующий `ix_analytics_site_visits_visitor_time` покрывает оба запроса компактора, а сама таблица после включения фичи держит ~35 минут трафика. GIN на `pages` не нужен — внутри jsonb никто не ищет.

## VS-002 — Чистая логика сессионизации и агрегации + unit-тесты
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-27T23:41:42+04:00
- completed: 2026-07-27T23:46:46+04:00
- desc: `src/OpenRussiaRouter.BuildingBlocks/Analytics/`: `CompactableVisit` (DTO строки визита), `VisitSessionizer.Split(IEnumerable<CompactableVisit>, TimeSpan gap, bool splitOnUtcDayBoundary = true)` → список слайсов, `VisitSessionAggregator.Aggregate(slice, maxPages)` → `VisitSessionRow`. Граница сессии: разрыв **строго больше** gap ИЛИ смена календарных суток UTC (второе обязательно — иначе бот, стучащийся раз в 5 минут, даёт вечно открытую сессию, которая каждый прогон пересчитывается и растит `pages` без предела). Контракт зафиксировать в XML-doc и тестах: вход сортируется внутри метода по `(OccurredAt, Id)`; разрыв **ровно** gap — та же сессия; сутки сравниваются по `OccurredAt.UtcDateTime.Date`; пустой вход → пустой список. Агрегация: `entry/exit_path` — первый/последний по времени, `utm_*` — **первый непустой**, `distinct_id` — **последний непустой** (посетитель мог залогиниться в середине сессии), `is_authenticated` = OR, `referrer_host/kind`/`device_class`/`environment` — из первого визита, `duration_seconds` = end − start, `pages` обрезается до `MaxPagesPerSession`, а `page_view_count` считается по ВСЕМ визитам (не по обрезанным). Тесты `tests/OpenRussiaRouter.BuildingBlocks.Tests/Analytics/VisitSessionizerTests.cs` и `VisitSessionAggregatorTests.cs` — БД не нужна; кейсы в разделе «Verification» плана (в т.ч. пауза ровно 30:00 → одна сессия, несортированный вход даёт тот же результат, переход через 00:00 UTC).

## VS-003 — VisitSessionCompactor: транзакция, advisory-лок, insert+delete
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-27T23:50:15+04:00
- completed: 2026-07-27T23:56:36+04:00
- desc: `src/OpenRussiaRouter.BuildingBlocks/Analytics/VisitSessionCompactor.cs`, метод `CompactOnceAsync(DateTimeOffset nowUtc, string? after, CancellationToken)` → `CompactionResult(int Sessions, int DeletedVisits, string? LastVisitorHash)`. Cutoff = `now - (SessionGap + SafetyLag)`; SafetyLag закрывает задержку очереди и перекос часов реплик WebApp (`occurred_at_utc` ставит приложение, а не `default now()`). Одна итерация = одна транзакция на одном соединении: `pg_try_advisory_xact_lock(@ns, @id)` (константы в C#, не `hashtext()` — он нестабилен между мажорами; **только xact-версия**, `pg_advisory_lock` на пуле Npgsql течёт) → выбор батча `visitor_hash` с keyset `visitor_hash > @after` (иначе посетитель с вечно открытой сессией занимает начало батча и прогресса нет) → выборка ВСЕХ строк этих посетителей, включая свежее cutoff (разрыв между последней старой и первой новой строкой решает, закрыта ли хвостовая сессия; фильтр по cutoff здесь порезал бы живые сессии на фрагменты) → `VisitSessionizer`+`Aggregator` в C# → оставить только сессии с `ended_at_utc < cutoff` → array-unnest insert с `on conflict (visitor_hash, started_at_utc) do update` (merge, НЕ `do nothing`: при `do nothing` поздний флаш молча потерялся бы, а `delete` всё равно отработал) → `delete from analytics.site_visits where id = any(@ids)`. Гвард `select to_regclass('analytics.visit_sessions')`: миграция не применена → один warning и выход, а не исключение каждый тик (fail-open контракт всей `Analytics/`). SQL целиком — в разделе «Технические детали» плана. Тесты — `tests/OpenRussiaRouter.BuildingBlocks.Tests/Analytics/VisitSessionCompactorPostgresTests.cs` на Testcontainers (пакет `Testcontainers.PostgreSql` в этом тест-проекте уже есть, паттерн «Docker нет → тихо return» — см. `PostgresProductAnalyticsTests`); кейсы — в «Verification» плана.

## VS-004 — Воркер, опции и регистрация в BillingService
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-27T23:58:27+04:00
- completed: 2026-07-28T00:06:50+04:00
- desc: `VisitSessionCompactionWorker : BackgroundService` рядом с компактором; скелет — как у `WebApp/Services/Payments/PaymentReconciliationWorker.cs` (startup-задержка, `while` + `catch (OperationCanceledException) { break; }` + `catch (Exception) → LogError`, `Task.Delay(interval)`), внутри — цикл до `MaxIterationsPerRun` или пока `Sessions == 0`, с переносом `LastVisitorHash` в `@after`. Каждая итерация — **своя короткая транзакция**: длинная держала бы `xmin` и блокировала autovacuum на `site_visits`. Опции — вложенный класс `Compaction` в существующем `SiteVisitOptions` (новую корневую секцию не плодить): `Enabled=true`, `IntervalMinutes=5`, `SessionGapMinutes=30`, `SafetyLagMinutes=5`, `VisitorBatchSize=500`, `MaxIterationsPerRun=20`, `MaxPagesPerSession=200`, `SplitOnUtcDayBoundary=true`. Новый метод `AddSiteVisitCompaction(IServiceCollection, IConfiguration)` в `SiteVisitServiceCollectionExtensions.cs` — **отдельно от `AddSiteVisitTracking`** (тот WebApp-only, вызывается в каждой реплике); гейт тот же: Postgres-синк сконфигурирован (`ProductAnalytics.Enabled` + `Provider == "Postgres"` + непустой `ConnectionString`) и `Compaction.Enabled`. Вызов — в `BillingService/Infrastructure/Startup/Startup.cs` рядом с `AddProductAnalytics`: Billing владеет БД `orr_analytics`, катает её миграции, `NpgsqlDataSource` аналитической БД у него уже зарегистрирован, и он идёт в одной реплике. Секцию `SiteVisits` добавить в `BillingService/appsettings.json`. Тест регистрации по образцу `ProductAnalyticsRegistrationTests`: без синка / при `Enabled=false` hosted service не регистрируется. Первый прод-прогон — с уменьшенным `VisitorBatchSize` и наблюдением за размером таблицы и autovacuum: накопленная история схлопывается за часы, а не одним махом.

## VS-005 — VisitSessionReader: read-side для админки
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-28T00:09:02+04:00
- completed: 2026-07-28T00:12:11+04:00
- desc: `src/OpenRussiaRouter.WebApp/Services/SiteStats/VisitSessionReader.cs` — зеркалит `SiteVisitReader.cs`: nullable `NpgsqlDataSource` + `UserPseudonymizer`, свойство `IsEnabled`, тот же disabled-режим (пустой результат вместо исключения, страница рисует баннер). `QueryAsync(VisitSessionQuery)` → `VisitSessionQueryResult`: фильтры `userId` (→ `distinct_id` → свежайший `visitor_hash`), `visitorHash`, `from`/`to` по `started_at_utc`, `includeBots` (по умолчанию `and device_class <> 'bot'`), пагинация 50, сортировка `started_at_utc desc`; `pages` в списке **не выбирать**, только агрегатные колонки. `GetByIdAsync(long id)` → `VisitSessionDetails` с распарсенным `pages` (`System.Text.Json`). DTO `VisitSessionEntry.cs`/`VisitSessionQuery.cs` рядом по образцу `VisitHistoryEntry.cs`. Регистрация — `AddSingleton<VisitSessionReader>` в `WebApp/Program.cs` фабрикой с nullable-зависимостями, ровно как уже сделано для `SiteVisitReader`.

## VS-006 — Страница /admin/sessions: список сессий
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-28T00:13:47+04:00
- completed: 2026-07-28T00:32:00+04:00
- desc: `WebApp/Pages/Admin/Sessions.cshtml(.cs)` — голый `@page`, маршрут выводится из пути файла → `/admin/sessions` (кебаб-кейс сам не появится). Layout `_AdminLayout`, `[Authorize(Roles = "Admin")]`, наследует `PageModelBase`. Карточка фильтра (User ID / Visitor hash / From / To / чекбокс «Show bots» — боты схлопываются наравне со всеми, но по умолчанию скрыты), пагинация и хелперы `BuildPageUrl`/`HasActiveFilter`/`ParseDate` — переносятся из `Pages/Admin/Visitors.cshtml(.cs)` как есть. Колонки: `Started | Duration | Visitor | Entry → Exit | Pages | Source / UTM | Device | Auth`; строка кликабельна на `/admin/sessions/{id}`. Имена пользователей по `distinct_id` — тем же способом, что `VisitorsModel.BuildUserNameMapAsync`. Пункт `Sessions` в сайдбаре — `Pages/Shared/_AdminLayout.cshtml` рядом с существующим `Visitors` (~строка 111). Локализация НЕ нужна: страницы админки в этом проекте написаны английскими литералами, `.resx` для них не заводятся. Тесты — `tests/OpenRussiaRouter.WebApp.Tests`, по образцу `VisitorsPageTests.cs` (ParseDate / HasActiveFilter / BuildPageUrl / арифметика пагинации).

## VS-007 — Страница /admin/sessions/{id}: детализация сессии
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-28T10:39:05+04:00
- completed: 2026-07-28T10:43:06+04:00
- desc: `WebApp/Pages/Admin/SessionDetails.cshtml(.cs)` с **явной** директивой `@page "/admin/sessions/{id:long}"` (параметры маршрута сами не появятся). Шапка с параметрами сессии (visitor_hash, время начала/конца, длительность, устройство, источник, UTM, auth + имя пользователя) и таблица `pages`: время, `+мм:сс` от начала сессии, путь. Ссылки «все сессии этого посетителя» (`/admin/sessions?visitorHash=`) и на сырой хвост (`/admin/visitors?visitorHash=`). Если `page_view_count > pages.Length` — подпись «показаны первые N из M» (обрезка детектируется именно так, отдельной колонки-флага нет). Несуществующий id → `NotFound()`, а не 200 с пустой страницей (тот же дефект, что чинила T-064).

## VS-008 — Фолбэк резолва visitor_hash в SiteVisitReader
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-28T10:45:01+04:00
- completed: 2026-07-28T10:46:32+04:00
- desc: `WebApp/Services/SiteStats/SiteVisitReader.cs`, `FindVisitorHashByDistinctIdAsync` ищет только в `analytics.site_visits` — после включения компакта `/admin/visitors?userId=` перестанет находить посетителя уже через ~40 минут, то есть ломается главный сценарий страницы «кто это был до регистрации». Добавить фолбэк на `analytics.visit_sessions` (`where distinct_id = :distinctId order by started_at_utc desc limit 1`), если в `site_visits` ничего не нашлось. Зависит от VS-001.

## VS-009 — Перецелить панели Grafana на visit_sessions
- status: done
- priority: medium
- created: 2026-07-28T00:30:00Z
- started: 2026-07-28T10:48:41+04:00
- completed: 2026-07-28T10:54:53+04:00
- desc: Секция «Посетители сайта» дашборда `ORR Users`, **одинаковые правки в двух файлах**: `deploy/compose/grafana/dashboards/users.json` и `infra/kustomize/base/infra/dashboards/users.json` (10 панелей, все сейчас читают `analytics.site_visits` и после компакта показывают только последние ~35 минут). Замены: «Уникальных посетителей» — `count(distinct visitor_hash)` по `started_at_utc`; «Просмотров страниц» и «Просмотры по дням» — `sum(page_view_count)` вместо `count(*)`; «Доля залогиненных» — `distinct visitor_hash where distinct_id is not null` по `visit_sessions`; «Доля ботов» — `sum(page_view_count) filter (where device_class='bot') / sum(page_view_count)`; «Новые/вернувшиеся» и «Посетители и регистрации по дням» — `date_trunc('day', started_at_utc)`; «Каналы привлечения» / «Страницы входа» / «UTM-метки» — `distinct on (visitor_hash) ... order by visitor_hash, started_at_utc` по `visit_sessions`, поля `entry_path`/`referrer_kind`/`utm_*` берутся прямо из строки (дешевле, чем сейчас: агрегат уже посчитан). В `description` секции — там уже есть блок про ограничения NAT/XFF — дописать, что последние ~35 минут ещё не схлопнуты и в панели не попадают (метрики дневные, для них незаметно). Делать после VS-004, когда в таблице появятся данные.

## VS-010 — Документация и запись известных пробелов
- status: done
- priority: low
- created: 2026-07-28T00:30:00Z
- started: 2026-07-28T10:57:05+04:00
- completed: 2026-07-28T11:00:48+04:00
- desc: `BuildingBlocks/CLAUDE.md` — в раздел «Трафик сайта» дописать компактор: правило границы сессии, где живёт воркер и почему в BillingService, ключи `SiteVisits:Compaction:*`. `WebApp/CLAUDE.md` — раздел про `/admin/sessions` и `/admin/sessions/{id}`, оговорка что `/admin/visitors` теперь показывает только несхлопнутый хвост, строки в карте маршрутов. `BillingService/CLAUDE.md` — воркер компакта в списке hosted services. Корневой `CLAUDE.md` — новые страницы в карте WebApp; в разделе «Лимиты, метрики, продуктовая аналитика» — что `site_visits` теперь эфемерна, а долгоживущее хранилище трафика это `analytics.visit_sessions`. В `TODO.md` — два пункта: (1) у `analytics.visit_sessions` нет retention; (2) `IProductAnalyticsErasure.PurgeUserAsync` её не чистит, как сегодня не чистит и `site_visits` — пре-существующий пробел, в объём этой работы не входит. Туда же записать известное искажение: флаш очереди с задержкой больше `SafetyLag` вставит визит в уже схлопнутую сессию — при совпадении с `started_at_utc` домержится через `on conflict`, иначе появится сессия-фрагмент из одного просмотра (документировать рядом с оговорками про NAT и `X-Forwarded-For`).
