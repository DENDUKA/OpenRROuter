# WebApp — CLAUDE.md

ASP.NET Core **Razor Pages** (не Blazor) — публичный сайт, кабинет пользователя и админка. **HTTP (порт 5004)**, cookie-аутентификация, локализация ru/en. К Auth/Billing/ControlPlane/Collector/Content ходит gRPC-клиентом; к ApiGateway, нодам 9Router, OpenRouter, ЦБ РФ, Rollypay и Brevo — HTTP. **Доменной БД нет** — единственное прямое подключение к Postgres это аналитический синк `orr_analytics` (визиты + продуктовые события).

## Bootstrap ([Program.cs](Program.cs))

- `AddSeqLogging("webapp")` + `AddOpenTelemetryMetrics` (`MapOpenTelemetryEndpoints`), `GET /health`.
- **Локализация**: `AddLocalization(ResourcesPath = "Resources")` + `AddViewLocalization` +
  DataAnnotations через `SharedResource`. Культуры — из
  [`SupportedCultures`](Services/Localization/SupportedCultures.cs) (`ru` по умолчанию, `en`),
  провайдеры только **cookie → Accept-Language** (query-string нет).
  `UseRequestLocalization()` стоит после `UseStaticFiles` и **до** `UseAuthentication`.
- **Cookie-auth**: основная схема `Cookies` (LoginPath/AccessDeniedPath `/Login`, HttpOnly,
  SameSite=Lax, 30 дней, sliding) + вторая схема `External` (`.orr.ext`) для OAuth.
- **Внешний логин регистрируется условно** — Google при заданных `Google:ClientId`/`ClientSecret`
  (callback `/signin-google`), Yandex при `Yandex:ClientId`/`ClientSecret` (callback `/OAuth-yandex`),
  оба с `SignInScheme = "External"`. Без гварда OAuth-хендлер падает 500 на `/Login`
  («ClientId option must be provided»).
- **gRPC-клиенты**: Auth(5001), Billing(5002), ControlPlane(5003), Collector(5005), Content(5007) —
  keep-alive `SocketsHttpHandler` + `GrpcDeadlineInterceptor` (`GrpcServices:CallDeadlineSeconds`,
  дефолт 30). Billing и Content дополнительно с `ServiceAuthClientInterceptor`:
  **`ServiceAuth:BillingToken` обязателен — без него сервис падает на старте**; Content по умолчанию
  переиспользует те же caller/token (`ServiceAuth:ContentCaller`/`ContentToken` переопределяют),
  и у его канала лимит сообщения поднят до 8 MiB (картинки инструментов ≤ 1 MiB).
- **Именованные `HttpClient`**: `ApiGateway` (`ApiGatewayUrl`, 120с), `RouterNode`
  (HTTP/1.1 exact, 20с), `DirectNodeChat` (HTTP/1.1, 120с — прямой чат с нодой из `/admin/nodes`),
  `OpenRouterDirect` (`https://openrouter.ai`, 20с), `Cbr`, клиент Rollypay, клиент Brevo,
  `YandexPay` (sandbox Merchant API, 15с).
- `AddEmailInfrastructure`, `AddProductAnalytics` + `AddSiteVisitTracking` (BuildingBlocks),
  currency-слой, платежи, rate-limit политика вебхука.
- **Hosted services**: `EmailDispatchWorker`, `CbrRateRefresher`, `PaymentReconciliationWorker`,
  `ReasoningEnrichmentWorker`.
- **`UseForwardedHeaders` — самый первый middleware** (XFF/Proto/Host от Caddy, `KnownNetworks`/
  `KnownProxies` очищены, `ForwardLimit = 1`). Обрабатывается только ближайший hop Caddy: это не даёт
  подставленным клиентским частям `X-Forwarded-For` создавать новые bucket'ы rate limit вебхука, сохраняя
  forwarding схемы и host для OAuth `redirect_uri` и абсолютных URL.
- **Data Protection** персистится в файловое хранилище по пути `DataProtection:KeyRingPath`
  (env `DataProtection__KeyRingPath`, в compose — named volume `webapp_keys` →
  `/var/lib/orr/dataprotection-keys`). Без этого ключи живут в ephemeral-ФС контейнера, каждый
  рестарт генерирует новое кольцо → выкидывает залогиненных (cookie-auth ticket защищён этими
  ключами) и плодит `AntiforgeryValidationException` ("key {guid} was not found in the key ring")
  на оставшихся антифоргери-куках. `SetApplicationName("OpenRussiaRouter.WebApp")` фиксирует
  дискриминатор. В unit-тестах (`WebAppFactory`) путь пуст — антифоргери там глобально выключен.
- `PaymentStartupGuard.Validate(...)` сразу после `builder.Build()`.
- **Security headers** (`Program.cs`): сразу после `UseForwardedHeaders` — `UseHsts()`
  (HSTS, работает только для HTTPS, localhost исключён ASP.NET Core по умолчанию)
  + inline middleware с `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: strict-origin-when-cross-origin`,
  `Content-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; script-src 'self' 'unsafe-inline' https://pay.yandex.ru; frame-src https://pay.yandex.ru; connect-src 'self' https://pay.yandex.ru; frame-ancestors 'none'`.
  Это первый эшелон защиты clickjacking и второй эшелон для T-013 (XSS через markdown).
  `UseHsts` автоматически неактивен в Development.
- Маппинг эндпоинтов: `MapRazorPages`, `MapControllers`, `MapOpenTelemetryEndpoints`,
  `PaymentWebhookEndpoint.Map`, `AlertWebhookEndpoint.Map`. `UseRateLimiter()` — **до** маппинга.

## Структура

| Папка | Содержимое |
|---|---|
| `Pages/` | Razor Pages (`.cshtml` + `.cshtml.cs`), [`PageModelBase`](Pages/PageModelBase.cs) (`CurrentUser`). Layout'ы: `_Layout` (общий), `_CatalogLayout` (публичные), `_WorkspaceLayout` (кабинет), `_AdminLayout` (админка), партиал `_IntegrationExamples` |
| `Api/` | Minimal-API эндпоинты: [`PaymentWebhookEndpoint`](Api/PaymentWebhookEndpoint.cs), [`AlertWebhookEndpoint`](Api/AlertWebhookEndpoint.cs) |
| `Controllers/` | MVC-контроллеры для того, что нужно с любой страницы или отдаёт бинарь: `PreferencesController`, `PublicProviderLogoController`, `PublicToolLogoController`, `PublicToolImageController`, `Admin/ProviderLogoController`, `Admin/GatewayTestController` |
| `Services/Auth` | `AuthProvider` (gRPC→Auth) + DTO, `AuthException` |
| `Services/Billing` | `BillingProvider` (gRPC→Billing) + DTO (balance, usage, models, providers, anomalies, tags, payments, лимиты ключей), `PlatformSettingKeys`, `BillingException` |
| `Services/Content` | `ContentProvider` (gRPC→Content) — каталог Tools, логотипы/картинки, `ContentException` |
| `Services/ControlPlane` | `ControlPlaneProvider` (gRPC) + `RouterNodeApiProvider` (прямой HTTP к admin API нод и `/v1/chat/completions`) |
| `Services/Collector` | `CollectorProvider` (gRPC→Collector) |
| `Services/Payments` | Чекаут, провайдеры, вебхук-процессор, гвард, воркер сверки, `Rollypay/`, `YandexPay/` |
| `Services/Currency` | Display-only слой курсов ЦБ РФ: `CbrClient`, `CbrRateSnapshot`, `CbrRateRefresher`, `CbrExchangeRateProvider`, `CurrencyFormatter`, `DisplayCurrency` |
| `Services/Enrichment` | `OpenRouterCatalogClient` (обогащение каталога моделей), `ReasoningEnrichmentWorker` |
| `Services/Legal` | `LegalDocumentService` — рендер `docs/mandatory/*.md` в страницы `/legal/*` |
| `Services/Markup` | `MarkdownRenderer` (санитизация недоверенного markdown), `MonogramSvg` (фолбэк-логотип) |
| `Services/SiteStats` | `SiteVisitTrackingMiddleware` (запись визитов в `analytics.site_visits`), `SiteVisitReader` (несхлопнутый хвост для `/admin/visitors`), `VisitSessionReader` + DTO `VisitSessionEntry`/`VisitSessionQuery` (сессии для `/admin/sessions`) |
| `Services/State` | `UserSession`, `ClaimsExtensions` (`GetUserId`, `GetSessionToken`); рядом `Services/ApiBaseUrlResolver.cs`, `Services/ColorContrast.cs` |
| `Email/` | Очередь + воркер + два транспорта (SMTP/Brevo API), шаблоны, рассылки |
| `Resources/` | `.ru.resx`/`.en.resx`: `SharedResource` + per-page ресурсы, путь зеркалит путь страницы (`Resources/Pages/Workspace/Credits.*.resx`) |
| `Support/` | `SupportSettings` (секция `Support`) для формы `/support` |
| `wwwroot/` | `app.css`, локальный `bootstrap/`, `favicon.png`, `icons/`, `js/tool-editor.js`. Своего фронтенд-билда нет, CDN не используется |

## Карта маршрутов

**Кебаб-кейс не выводится автоматически**: маршрут — это либо явная директива `@page "..."`, либо
PascalCase-путь файла. Источник истины — директивы `@page`; в корневом `CLAUDE.md` часть путей
записана в «причёсанном» виде.

| Группа | Маршруты |
|---|---|
| Публичные | `/` (лендинг, `Pages/Index`), `/models`, `/models/{provider}/{slug}`, `/providers`, `/providers/{id}`, `/tools`, `/tools/{id}`, `/docs`, `/support`, `/legal/offer`, `/legal/privacy`, `/error` |
| Аутентификация | `/login`, `/signup` → `/verify-email`, `/forgot-password` → `/reset-password`, `/invite/{token}`, `/logout`, `/login/externalcallback` |
| Кабинет (`[Authorize]`) | `/workspace` (редирект на `/workspace/apikeys`), `/workspace/apikeys`, `/workspace/apikeys/limits`, `/workspace/activity`, `/workspace/credits`, `/workspace/paymentsuccess`, `/workspace/settings` |
| Админка (`[Authorize(Roles = "Admin")]`) | `/admin`, `/admin/users`, `/admin/users/{userId}/edit`, `/admin/topups`, `/admin/models`, `/admin/models/sync`, `/admin/tags`, `/admin/providers`, `/admin/tools`, `/admin/tools/edit/{id?}`, `/admin/nodes`, `/admin/nodes/{nodeId}`, `/admin/nodes/{nodeId}/edit`, `/admin/nodes/{nodeId}/provider-limits`, `/admin/anomalies`, `/admin/requests`, `/admin/broadcast`, `/admin/gatewaytest`, `/admin/visitors`, `/admin/sessions` (голый `@page`, путь из имени файла `Admin/Sessions.cshtml`), `/admin/sessions/{id:long}` (явная директива `@page "/admin/sessions/{id:long}"` в `Admin/SessionDetails.cshtml` — иначе путь был бы `/admin/sessiondetails`) |
| Платежи | `/pay/mock/{paymentId}` (dev-заглушка, `[Authorize]`), `POST /workspace/credits/yandexpay/checkout` (admin-only, JSON), `POST /v1/webhook` (Yandex Pay JWT ES256, анонимный) |
| Контроллеры | `POST /preferences/currency` (`[Authorize]`), `POST /preferences/language` (анонимно, ставит culture-cookie), `GET /providers/logo/{id}`, `GET /tools/logo/{id}` (фолбэк — монограмма), `GET /tools/image/{id:guid}`, `GET /admin/providers/logo/{id}`, `POST /admin/gatewaytest/stream` (SSE) |
| Служебные | `POST /webhook/{provider}`, `POST /internal/alerts/grafana`, `GET /health`, OTel-эндпоинты |

Все POST-контроллеры под `[ValidateAntiForgeryToken]`.

## Платежи ([Services/Payments](Services/Payments))

Границы ответственности жёсткие (см. корневой CLAUDE.md), в коде это выглядит так:

- **Страницы вызывают только [`PaymentCheckoutService`](Services/Payments/PaymentCheckoutService.cs)**,
  никогда `BillingProvider.CreatePayment` / `AttachProviderRef` напрямую. Флоу: Billing создаёт строку
  и `payment_id` (= `order_id`, считает `credited_usd` по своей FX-котировке) → провайдер отдаёт свой id
  и pay URL → `AttachProviderRef` атомарно привязывает id/URL/expiry.
- **Кросс-курсовая конверсия — по котировкам Billing** (`GetFxQuoteAsync`), а не по CBR-снапшоту
  WebApp: если провайдер пинит `SettlementCurrency` (Rollypay = RUB), Billing позже сверяет
  подтверждённую провайдером сумму со своей записью, и расхождение курсов даст аномалию
  `payment_amount_mismatch` вместо зачисления.
- `Payments:MinTopUpUsd`/`MaxTopUpUsd` — **только UI-подсказка**, авторитетная проверка в Billing.
- Первый топап с бонусом: `PlatformSettingKeys.PromotionsFirstTopUp*` из Billing +
  `HasUserHadPaidTopUpAsync`; ошибка чтения настройки просто отключает бонус.
- **Реестр провайдеров** `PaymentProviderRegistry` + `IPaymentProvider`: `MockPaymentProvider`
  регистрируется **только в Development**, `RollypayPaymentProvider` — всегда, но активен лишь при
  `Payments:ActiveProvider="rollypay"`, `YandexPayPaymentProvider` — всегда, но используется
  только через admin-only эндпоинт чекаута (не зависит от `ActiveProvider`). Пустой `ActiveProvider`
  просто выключает публичные платежи.
- **[`PaymentStartupGuard`](Services/Payments/PaymentStartupGuard.cs)** вне Development падает при:
  `ActiveProvider="mock"`, неизвестном провайдере и **`Payments:Rollypay:TestMode=true`**
  (тестовый платёж денег не двигает, но Billing зачисляет реальные USD). Не ослаблять.
- **Вебхук** `POST /webhook/{provider}`: анонимный, политика rate-limit `payment-webhook`
  (per-IP fixed window 60/мин), тело ≤ 64 KB (`LimitedStream` работает и без `Content-Length`),
  **verify-then-parse по сырому телу** (`RollypayWebhookVerifier`:
  `HMAC-SHA256(secret, "{X-Timestamp}.{raw_body}")` в `X-Signature`) → 401 при провале
  подписи, 400 при неразбираемом теле, 200 `ok`/`ignored`, 422 при отказе Billing.
  Незаконфигуренный `SigningSecret` → все вебхуки 401 (fail safe), пустой `ApiKey` → исходящие
  вызовы бросают.
- **Yandex Pay (sandbox)** — альтернативный платёжный провайдер (только карта, только sandbox,
  только для админов). Состав: `YandexPayPaymentProvider` (IPaymentProvider, Name=yandexpay,
  SettlementCurrency=RUB), `YandexPayClient` (HTTP к Merchant API, sandbox URL,
  `Authorization: Api-Key {merchantId}`), `YandexPayJwkProvider` (кеш JWK-ключей из
  `https://sandbox.pay.yandex.ru/api/jwks`), `YandexPayWebhookVerifier` (ES256 JWT
  верификация + `merchantId` валидация + подтягивание суммы из API для `CAPTURED`).
  Эндпоинты: `POST /workspace/credits/yandexpay/checkout` (admin-only, JSON),
  `POST /v1/webhook` (JWT ES256, анонимный). Включается через `Payments:YandexPay:Enabled=true`
  + `Payments:YandexPay:MerchantId`. Sandbox merchantId = API key. Секреты — только env/secret.
  `_WorkspaceLayout` не поддерживает `@section Scripts` — скрипт Yandex Pay инлайнов.
  Механика — **платёжная ссылка без Web SDK**: SDK не подключён и не нужен, кнопка своя, с
  официальным лого (`wwwroot/icons/yandex-pay.png`, стиль `.btn-yapay` — белая плашка, потому что
  бренд-лого тёмное на прозрачном). Записи `pay.yandex.ru` в `script-src`/`frame-src`/`connect-src`
  сейчас ни на что не влияют — оплата это обычный top-level редирект на `paymentUrl`; они
  понадобятся, только если появится SDK или бейджи (тогда же придётся добавить `img-src`).
- **Ответ Yandex Pay вебхука — свой контракт, не общий с Rollypay.** Успех — 200
  `{"status":"success"}`, отказ — не-200 с `reasonCode` из закрытого списка Яндекса
  (`UNAUTHORIZED`, `ORDER_NOT_FOUND`, `ORDER_AMOUNT_MISMATCH`, `CONFLICT`); маппинг
  Billing-кодов — `YandexPayWebhookEndpoint.ToReasonCode`. **`RejectedPermanent` намеренно
  отдаёт 200**: Яндекс читает любой не-2xx как «повторить» и долбит 24 часа с backoff, а вердикт
  Billing окончателен — причина уезжает в `LogError`, в теле ack'а поля под неё нет.
  `MapNotification` обязан совпадать с `YandexPayPaymentProvider.MapStatus`: статус, разобранный
  там, но не здесь, ждёт целый цикл реконсиляции (`VOIDED`, `PARTIALLY_REFUNDED` — как раз этот
  случай).
- **`PaymentReconciliationWorker`** добирает статусы: раз в `Reconciliation:Interval` берёт
  `Created`-платежи старше `OlderThanMinutes` через Billing (`SKIP LOCKED` + лиза), спрашивает
  провайдера и **прогоняет ответ через тот же `PaymentWebhookProcessor`**, замапив статус
  в webhook-подобное событие (succeeded/expired/…). Отдельного RPC на release лизы нет — успешный
  переход состояния в Billing её снимает; иначе лиза истекает сама.

## Валюта отображения ([Services/Currency](Services/Currency))

Секция `ExchangeRates` (`Source`, `RefreshHourMsk`, `MarkupPercent`, `Fallback.RubPerUsd/RubPerEur`).
`CbrRateRefresher` раз в сутки обновляет `CbrRateSnapshot`, `CurrencyFormatter` печатает баланс
в RUB/USD/EUR. **Это слой только отображения**: реальные деньги, зачисления и суммы чекаута считает
BillingService своей котировкой.

## Email ([Email/](Email))

- `AddEmailInfrastructure`: `EmailSettings` (секция `Email`), singleton-очередь
  `EmailDispatchQueue`/`IEmailDispatchQueue` + `EmailDispatchWorker`, `EmailBroadcastService`.
- **Транспорт по `Email:Transport`**: `brevoApi` → [`BrevoApiEmailSender`](Email/BrevoApiEmailSender.cs)
  (HTTPS `api.brevo.com`, ключ в `Email:BrevoApiKey`), иначе (дефолт `smtp`) MailKit
  [`SmtpEmailSender`](Email/SmtpEmailSender.cs). **Креды SMTP умышленно не в `EmailSettings`** —
  `SmtpUsername`/`SmtpPassword` читаются из секретов/env, чтобы не утечь в лог сериализацией опций.
  На прод-хосте исходящий SMTP закрыт → там обязателен `Transport=brevoApi`.
- Воркер дренит очередь последовательно: до **3 попыток** с backoff 1с/2с/4с, на финальном провале
  сообщение **отбрасывается** (log error), после каждого — пауза `EmailSettings.BatchDelayMs`
  (дефолт 1000мс ≈ 1 msg/s). Без настроенного транспорта коды верификации/инвайты не уходят.
- Через ту же очередь идут: письма верификации/инвайтов/сброса пароля (`EmailTemplates`),
  рассылка из `/admin/broadcast`, форма `/support` (Reply-To = отправитель, тикеты нигде не хранятся)
  и алерты Grafana.

## Алерт-вебхук Grafana ([AlertWebhookEndpoint.cs](Api/AlertWebhookEndpoint.cs))

`POST /internal/alerts/grafana` — почему так, а не SMTP-contact point, см. корневой CLAUDE.md.

- Анонимный, но гейтится shared-токеном `Alerting:WebhookToken`: читается из
  `Authorization: Bearer` **или** `X-Alert-Token`, сравнение constant-time.
- **Без `Alerting:WebhookToken`/`Alerting:EmailTo` эндпоинт отдаёт 503**, а не остаётся открытым.
  Обеих ключей в `appsettings.json` нет — только env/secret.
- Тело ≤ 256 KB. Парсинг защитный: сломанный JSON уезжает письмом «как есть» (обрезка до 4000
  символов), алерт не теряется.
- В письмо идут `annotations.summary` (фолбэк — `alertname`), `description` и **доменные лейблы**;
  выкидываются `alertname`, `grafana_folder`, `severity`, `connection_id`, `node_id`, всё `__*`
  и внутренний `valueString`.

## Трекинг визитов ([SiteVisitTrackingMiddleware.cs](Services/SiteStats/SiteVisitTrackingMiddleware.cs))

Считает уникальных посетителей сайта, **включая незарегистрированных**, и источник перехода —
это данные для секции «Посетители сайта» дашборда Grafana `ORR Users`.

- Пишутся только **GET**-запросы, строка ставится **после** ответа и только если он оказался HTML
  (`text/html`, статус < 400) — так отсеиваются статика, редиректы, API и вебхуки без списка
  расширений. Плюс явный чёрный список префиксов (`/health`, `/metrics`, `/internal`, `/webhook`,
  `/.well-known`).
- Посетитель = `HMAC-SHA256(ProductAnalytics:IdentitySalt, ip + "|" + user-agent)`
  (`VisitorIdHasher`). **Сырой IP не сохраняется, кук не ставим** — согласие не требуется.
  Соль стабильная: без неё нельзя отличить вернувшегося посетителя от нового.
  Ограничения: один NAT = один посетитель; `X-Forwarded-For` подделывается клиентом.
- У залогиненного визита заполняется `distinct_id` — тот же `UserPseudonymizer`, что и в
  `analytics.events`, поэтому воронка «визит → регистрация» считается одним SQL.
- Что ещё складывается в строку: `NormalizePath` схлопывает id-подобные сегменты в `{id}`
  (`/models/{id}`, `/invite/{id}`), из referer берётся **только host** + грубая корзина
  (direct/internal/search/social/external), `utm_source|medium|campaign` (≤100 символов, lowercase),
  класс устройства (desktop/mobile/tablet/**bot** — по маркерам в UA; пустой UA считается ботом).
- Очередь + фоновой флаш (`SiteVisitQueue` → `SiteVisitFlushWorker` в BuildingBlocks):
  очередь переполнена — визит отбрасывается, ошибка вставки — батч дропается с логом.
  Рендер страницы не блокируется никогда, исключение в middleware проглатывается с warning.
- Выключается `SiteVisits:Enabled=false`; без настроенного `ProductAnalytics`-синка
  middleware вообще не регистрируется (`Program.cs` проверяет наличие `SiteVisitQueue` в DI).
- Схема — `db/migrations/analytics/002_site_visits.sql`, применяет **BillingService**
  (он владеет базой `orr_analytics`).
- **`site_visits` — эфемерный буфер**: компакт в BillingService (`VisitSessionCompactionWorker`,
  BuildingBlocks) схлопывает визиты в `analytics.visit_sessions` и **удаляет исходные строки**,
  так что в таблице держится ~35 минут трафика. Долгоживущее хранилище трафика —
  `visit_sessions`; всё, что читает историю визитов дольше получаса, обязано смотреть туда.

## История визитов в админке ([Visitors.cshtml](Pages/Admin/Visitors.cshtml))

`/admin/visitors` — read-сторона той же таблицы `analytics.site_visits`. Использует
[`SiteVisitReader`](Services/SiteStats/SiteVisitReader.cs), который инжектит уже
зарегистрированный `NpgsqlDataSource` (тот же, что пишет flush-воркер) и `UserPseudonymizer`.

- ⚠️ **Страница показывает только несхлопнутый хвост** (~35 минут): всё старше уже переехало в
  `analytics.visit_sessions`, а исходные строки удалены. Оставлена намеренно — это сырой поток для
  отладки самого трекинга. Историю за период смотрят на `/admin/sessions`.
- **Сценарий «кто это до регистрации»**: админ передаёт `?userId=` → ридер вычисляет
  `distinct_id = pseudonymizer.ComputeDistinctId(userId)`, находит самый свежий визит с этим
  `distinct_id`, берёт оттуда `visitor_hash`, и тащит **все** визиты с этим `visitor_hash`
  (анонимные до регистрации + залогиненные после). Альтернатива — прямой фильтр
  `?visitorHash=`. Дополнительно `?from=`/`?to=` (UTC, `yyyy-MM-dd`).
- **Резолв `visitor_hash` по `distinct_id` — с фолбэком на `visit_sessions`.** Сначала
  `site_visits` (там самый свежий хеш, и только там живёт ещё открытая сессия), при пустом
  результате — `analytics.visit_sessions` (`order by started_at_utc desc limit 1`). Без фолбэка
  фильтр `?userId=` перестал бы находить кого-либо старше ~40 минут. Отсутствие таблицы
  (`PostgresException`, `SqlState == UndefinedTable`) гасится в warning + `null` — миграция может
  быть не применена.
- **Пагинация**: `?p=0..N`, `PageSize = 50`, `BuildPageUrl` сохраняет все фильтры.
- **Disabled-режим**: если аналитический sink не сконфигурирован (нет `NpgsqlDataSource` в DI),
  `SiteVisitReader.IsEnabled = false` и страница рисует жёлтый баннер «Analytics sink is not
  configured» вместо запроса. Регистрация в `Program.cs` — `AddSingleton<SiteVisitReader>` с
  фабрикой, отдающей nullable-зависимости.
- **Privacy**: `visitor_hash` уже HMAC-псевдоним, raw IP не виден; `distinct_id` показывается
  только когда есть фильтр по `userId` — для анонимных запросов остаётся только `visitor_hash`.
- Сетку колонок и пагинацию см. в `Pages/Admin/TopUps.cshtml` — паттерн общий для админских
  страниц с постраничным списком.

## Сессии посетителей в админке ([Sessions.cshtml](Pages/Admin/Sessions.cshtml))

`/admin/sessions` + деталь `/admin/sessions/{id:long}` — read-сторона `analytics.visit_sessions`,
той таблицы, куда компакт BillingService складывает схлопнутые визиты. Ридер —
[`VisitSessionReader`](Services/SiteStats/VisitSessionReader.cs), зеркалит `SiteVisitReader`:
nullable `NpgsqlDataSource` + `UserPseudonymizer`, `IsEnabled`, тот же disabled-режим (пустой
результат вместо исключения, страница рисует жёлтый баннер). Регистрация — `AddSingleton` фабрикой
с nullable-зависимостями в `Program.cs`.

- **Это основная страница истории трафика**, `/admin/visitors` — только сырой хвост. Фильтры:
  `?userId=` (через `distinct_id` → свежайший `visitor_hash` → все сессии этого посетителя),
  `?visitorHash=`, `?from=`/`?to=` (UTC, `yyyy-MM-dd`, по `started_at_utc`), `?bots=true`
  (**имя параметра `bots`**, не `includeBots`), `?p=` — пагинация по 50, сортировка
  `started_at_utc desc`. Хелперы `ParseDate`/`HasActiveFilter`/`BuildPageUrl` — те же, что на
  `/admin/visitors`.
- **Боты схлопываются наравне со всеми** (иначе «доля ботов» в Grafana станет несчитаемой), но в
  листинге по умолчанию скрыты (`device_class <> 'bot'`).
- **`pages` в листинге не выбирается** — это единственная неограниченная колонка таблицы; она
  читается только в `GetByIdAsync`. Неразбираемый jsonb деградирует в пустой список страниц +
  warning, шапка сессии остаётся.
- **Деталь**: несуществующий id → `NotFound()`, а не 200 с пустой страницей. Обрезка списка
  страниц определяется как `page_view_count > pages.Length` (флага в схеме нет) и показывается
  подписью «первые N из M». Ссылки ведут на все сессии посетителя (`/admin/sessions?visitorHash=`)
  и на сырой хвост (`/admin/visitors?visitorHash=`).
- **Локализации нет** — админские страницы в проекте написаны английскими литералами, `.resx` для
  них не заводятся.

## Фильтр пополнений в админке ([TopUps.cshtml](Pages/Admin/TopUps.cshtml))

`/admin/topups?userQuery=...` сначала сопоставляет запрос с пользователями через Auth. Отсутствие
фильтра представляется как `null` и загружает общий список из Billing; непустой фильтр без
совпадений возвращает пустую страницу **без** вызова Billing. Пустой список идентификаторов нельзя
передавать в Billing как отсутствие фильтра, иначе администратору раскроется чужая финансовая история.

## Конфигурация

| Ключ / секция | Назначение |
|---|---|
| `ApiGatewayUrl` | база для named-клиента `ApiGateway` (тест шлюза из админки) |
| `DataProtection:KeyRingPath` | путь к persistent-кольцу ключей Data Protection; пусто → ephemeral-дефолт (только локал/тесты) |
| `PublicApiBaseUrl` | фолбэк для `ApiBaseUrlResolver` (примеры интеграции, `/docs`) |
| `GrpcServices:{Auth,Billing,ControlPlane,Collector,Content}` | адреса сервисов; `CallDeadlineSeconds` — дедлайн всех gRPC-вызовов |
| `ServiceAuth:{BillingCaller,BillingToken,ContentCaller,ContentToken}` | s2s-креды; **`BillingToken` обязателен**, Content-пара по умолчанию наследует Billing |
| `Google:*`, `Yandex:*` | внешний логин; провайдер не регистрируется, пока не заданы оба значения |
| `Email:*` | транспорт, SMTP-хост/порт/SSL, отправитель, `BatchDelayMs`; секреты (`BrevoApiKey`, SMTP-логин/пароль) — только env/secret |
| `Support:{Inbox,TelegramUrl}` | получатель формы `/support` и альтернативный канал |
| `ProductAnalytics:*` | синк `orr_analytics` (`Enabled`, `ConnectionString`, `IdentitySalt`, `Environment`, размеры очереди) |
| `SiteVisits:{Enabled,QueueCapacity,BatchSize}` | трекинг визитов; вложенная секция `SiteVisits:Compaction:*` здесь не читается — компакт хостит BillingService |
| `ExchangeRates:*` | display-only курсы ЦБ РФ + фолбэки |
| `Payments:*` | `ActiveProvider`, `Min/MaxTopUpUsd`, `Mock.SharedSecret`, `Rollypay.*`, `YandexPay.*` (Enabled, Sandbox, MerchantId, SuccessRedirectUrl, FailRedirectUrl, OrderTtlSeconds), `Reconciliation.*` |
| `Alerting:{WebhookToken,EmailTo}` | алерт-вебхук; **в `appsettings.json` отсутствуют** |

`appsettings.Development.json` включает mock-провайдер, dev s2s-токен и dev-креды Yandex —
в прод это не уезжает (`PaymentStartupGuard` + отдельные секреты).

## Подводные камни

- **Только gRPC/HTTP-клиенты** — доменной БД у WebApp нет, вся логика в backend-сервисах.
  Единственное прямое подключение к Postgres — аналитический синк (визиты/события);
  для доменных данных так делать нельзя.
- **Новая строка UI = две `.resx`** (`.ru.resx` + `.en.resx`) в пути, зеркалящем путь страницы,
  иначе на второй культуре останется ключ. Новый маршрут — не забыть ссылку в соответствующем
  layout'е и что кебаб-кейс сам не появится (нужна директива `@page "..."`).
- **`RouterNodeApiProvider`** ходит в admin API нод (`/api/integrations/*`) со **значением
  `control_token` из ControlPlane-БД** (`Authorization: Bearer` + дублирующий `X-ORR-Node-Token`).
  Никакого маппинга токенов через конфиг здесь нет и не надо реинтродьюсить. Для чат-теста
  (`/admin/gatewaytest`, прямой чат в `/admin/nodes`) bearer'ом идёт первый активный `sk-or-*`
  ключ текущего админа.
- **`LegalDocumentService`** читает `docs/mandatory/{privacy-policy,public-offer}.md`, которые
  копируются в output через `<Content Include="../../docs/mandatory/*.md">` в
  [csproj](OpenRussiaRouter.WebApp.csproj), и подставляет плейсхолдеры `ORR_*`. Переименование или
  удаление файла ломает `/legal/*`; whitelist имён — в самом сервисе.
- **`MarkdownRenderer`** — для недоверенного markdown (описания моделей из OpenRouter, админские
  textarea): строгий pipeline с `DisableHtml()` + regex-скраббер. Скраббер не HTML-парсер, не
  полагаться на него как на единственную защиту.
- `ApiBaseUrlResolver`: `platform_settings` (админская настройка) → `PublicApiBaseUrl` → origin
  запроса. Ошибка Billing не валит страницу, просто уводит на фолбэк.
- `ReasoningEnrichmentWorker` крутится **в каждой реплике**, идемпотентен по `model_id` и **не
  перетирает** админскую разметку reasoning-возможностей (патчит только не отмеченные модели или
  отмеченные с пустым списком efforts).
- `UseForwardedHeaders` должен остаться первым middleware, иначе за Caddy ломаются OAuth и
  абсолютные ссылки. `ForwardLimit = 1` ограничивает обработку ближайшим Caddy hop, чтобы
  подставленный клиентом `X-Forwarded-For` не обходил per-IP rate limit платёжного вебхука;
  не фиксировать для этого Docker-подсеть и не убирать forwarding схемы/host.
- Внешний логин не появится, пока не заданы оба секрета соответствующего провайдера.
- **`AddMemoryCache()` — без `SizeLimit`, и возвращать его нельзя.** С заданным `SizeLimit`
  ASP.NET требует `Size` у **каждой** записи, а забытый `Size` не деградирует, а бросает
  `InvalidOperationException` прямо из `Set`/`GetOrCreateAsync` — так `/models` целиком уходила
  в «Failed to load public model catalog» из-за одной записи каталога. Кеш ограничивается только
  временем жизни записей (каталог нод 60 мин, список провайдеров 1 ч, логотипы 24 ч).

## Тесты

- **`tests/OpenRussiaRouter.WebApp.Tests`** — xUnit + FluentAssertions + Moq;
  [`WebAppFactory`](../../tests/OpenRussiaRouter.WebApp.Tests/WebAppFactory.cs) —
  `WebApplicationFactory<Program>` с глобально выключенным antiforgery, плюс
  `TestAuthenticationHandler` для залогиненных сценариев. Покрыто: платежи (чекаут, реестр, гвард,
  вебхук-эндпоинт и процессор, Rollypay-верификатор), email (очередь, воркер, шаблоны),
  локализация и переключение языка, юридические страницы/согласие/`LegalDocumentService`,
  трекинг визитов, markdown, курсы ЦБ, activity, support, страницы Tools.
- **E2E** — Playwright, opt-in (`tests/OpenRussiaRouter.E2ETests`). База —
  `ORR_E2E_BASE_URL` / `e2e.local.json` (дефолт `http://localhost:5004`), креды —
  `ORR_E2E_USER_EMAIL`/`ORR_E2E_USER_PASSWORD`; **без кредов тесты тихо скипаются**.
