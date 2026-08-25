# WebApp — CLAUDE.md

ASP.NET Core **Razor Pages** (не Blazor) — публичный сайт, кабинет пользователя и админка. **HTTP (порт 5004)**, cookie-аутентификация, локализация ru/en. К Auth/Billing/ControlPlane/Collector/Content ходит gRPC-клиентом; к ApiGateway, нодам 9Router, OpenRouter, ЦБ РФ, Rollypay и Brevo — HTTP. **Доменной БД нет** — единственное прямое подключение к Postgres это аналитический синк `orr_analytics` (продуктовые события).

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
  Канонический `redirect_uri` (`https://<host>/signin-google` и `https://<host>/OAuth-yandex`) принудительно
  нормализуется в `OnRedirectToAuthorizationEndpoint` без префикса локали `PathBase` (`/en`), чтобы
  провайдеры не выдавали `redirect_uri_mismatch`, а выбранная культура прокидывается через `AuthenticationProperties.Items["culture"]`
  и сохраняется в куку `.AspNetCore.Culture` в `ExternalCallback`.
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
- `AddEmailInfrastructure`, `AddProductAnalytics` (BuildingBlocks),
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
  Базовые записи аналитики (GA4: `*.google-analytics.com` + голый `analytics.google.com` +
  `googletagmanager.com`; Яндекс: `mc.yandex.ru|com`, `mc.webvisor.org|com`, `yastatic.net`,
  `wss://` в `connect-src`) дописываются условно по секции `Analytics` — см. раздел
  «Веб-аналитика» ниже.
  Это первый эшелон защиты clickjacking и второй эшелон для T-013 (XSS через markdown).
  `UseHsts` автоматически неактивен в Development.
- **Канонизация URL** — инлайн-middleware сразу **после `UseStaticFiles`**, отдаёт 301 через
  [`CanonicalUrlNormalizer`](Services/Seo/CanonicalUrlNormalizer.cs). Снимает хвостовой `/` и
  понижает регистр у **закрытого списка** статических публичных путей. Понижать регистр всего
  подряд нельзя: `/providers/logo/{id}`, `/tools/image/{guid}` и слаги моделей сравниваются
  ordinal ниже по стеку (у моделей своя канонизация — `ModelDetailRoute`). Только GET/HEAD:
  301 на POST выкинул бы тело запроса. После `UseStaticFiles` — чтобы статика отдавалась нетронутой.
- Маппинг эндпоинтов: `MapRazorPages`, `MapControllers`, `MapOpenTelemetryEndpoints`,
  `PaymentWebhookEndpoint.Map`, `AlertWebhookEndpoint.Map`, `SitemapEndpoint.Map`
  (динамический XML-sitemap, `Api/SitemapEndpoint.cs`). `UseRateLimiter()` — **до** маппинга.

## Структура

| Папка | Содержимое |
|---|---|
| `Pages/` | Razor Pages (`.cshtml` + `.cshtml.cs`), [`PageModelBase`](Pages/PageModelBase.cs) (`CurrentUser`). Layout'ы: `_Layout` (общий), `_CatalogLayout` (публичные), `_WorkspaceLayout` (кабинет), `_AdminLayout` (админка), партиал `_IntegrationExamples` |
| `Api/` | Minimal-API эндпоинты: [`PaymentWebhookEndpoint`](Api/PaymentWebhookEndpoint.cs), [`AlertWebhookEndpoint`](Api/AlertWebhookEndpoint.cs) |
| `Controllers/` | MVC-контроллеры для того, что нужно с любой страницы или отдаёт бинарь: `PreferencesController`, `PublicProviderLogoController`, `PublicToolLogoController`, `PublicToolImageController`, `Admin/ProviderLogoController`, `Admin/GatewayTestController` |
| `Services/Auth` | `AuthProvider` (gRPC→Auth) + DTO, `AuthException` |
| `Services/Billing` | `BillingProvider` (gRPC→Billing) + DTO (balance, usage, models, providers, anomalies, tags, payments, лимиты ключей), `PlatformSettingKeys`, `BillingException` |
| `Services/Catalog` | [`ModelDetailRoute`](Services/Catalog/ModelDetailRoute.cs) — единственный источник истины URL-контракта `/models/{provider}/{slug}` (построение ссылок, канонизация, матчинг старых форм) |
| `Services/Content` | `ContentProvider` (gRPC→Content) — каталог Tools, логотипы/картинки, `ContentException` |
| `Services/ControlPlane` | `ControlPlaneProvider` (gRPC) + `RouterNodeApiProvider` (прямой HTTP к admin API нод и `/v1/chat/completions`) |
| `Services/Collector` | `CollectorProvider` (gRPC→Collector) |
| `Services/Payments` | Чекаут, провайдеры, вебхук-процессор, гвард, воркер сверки, `Rollypay/`, `YandexPay/` |
| `Services/Currency` | Display-only слой курсов ЦБ РФ: `CbrClient`, `CbrRateSnapshot`, `CbrRateRefresher`, `CbrExchangeRateProvider`, `CurrencyFormatter`, `DisplayCurrency` |
| `Services/Enrichment` | `OpenRouterCatalogClient` (обогащение каталога моделей), `ReasoningEnrichmentWorker` |
| `Services/Legal` | `LegalDocumentService` — рендер `docs/mandatory/*.md` в страницы `/legal/*` |
| `Services/Markup` | `MarkdownRenderer` (санитизация недоверенного markdown), `MonogramSvg` (фолбэк-логотип) |
| `Services/State` | `UserSession`, `ClaimsExtensions` (`GetUserId`, `GetSessionToken`); рядом `Services/ApiBaseUrlResolver.cs`, `Services/SiteBaseUrlResolver.cs`, `Services/ColorContrast.cs` |
| `Email/` | Очередь + воркер + два транспорта (SMTP/Brevo API), шаблоны, рассылки |
| `Resources/` | `.ru.resx`/`.en.resx`: `SharedResource` + per-page ресурсы, путь зеркалит путь страницы (`Resources/Pages/Workspace/Credits.*.resx`) |
| `Support/` | `SupportSettings` (секция `Support`) для формы `/support` |
| `wwwroot/` | `app.css`, локальный `bootstrap/`, `favicon.png`, `icons/`, `js/tool-editor.js`, `robots.txt`, `og-image.png` (OG-баннер 1200×630). Своего фронтенд-билда нет, CDN не используется |

## Карта маршрутов

**Кебаб-кейс не выводится автоматически**: маршрут — это либо явная директива `@page "..."`, либо
PascalCase-путь файла. Источник истины — директивы `@page`; в корневом `CLAUDE.md` часть путей
записана в «причёсанном» виде.

| Группа | Маршруты |
|---|---|
| Публичные | `/` (лендинг, `Pages/Index`), `/models`, `/models/{provider}/{slug}`, `/providers`, `/providers/{id}`, `/tools`, `/tools/{id}`, `/docs`, `/support`, `/legal/offer`, `/legal/privacy`, `/error` |
| Аутентификация | `/login`, `/signup` → `/verify-email`, `/forgot-password` → `/reset-password`, `/invite/{token}`, `/logout`, `/login/externalcallback` |
| Кабинет (`[Authorize]`) | `/workspace` (редирект на `/workspace/apikeys`), `/workspace/apikeys`, `/workspace/apikeys/limits`, `/workspace/activity`, `/workspace/credits`, `/workspace/paymentsuccess`, `/workspace/settings` |
| Админка (`[Authorize(Roles = "Admin")]`) | `/admin`, `/admin/users`, `/admin/users/{userId}/edit`, `/admin/topups`, `/admin/models`, `/admin/models/sync`, `/admin/models/prices`, `/admin/tags`, `/admin/providers`, `/admin/tools`, `/admin/tools/edit/{id?}`, `/admin/nodes`, `/admin/nodes/{nodeId}`, `/admin/nodes/{nodeId}/edit`, `/admin/nodes/{nodeId}/provider-limits`, `/admin/anomalies`, `/admin/requests`, `/admin/broadcast`, `/admin/gatewaytest` |
| Платежи | `/pay/mock/{paymentId}` (dev-заглушка, `[Authorize]`), `POST /workspace/credits/yandexpay/checkout` (admin-only, JSON), `POST /v1/webhook` (Yandex Pay JWT ES256, анонимный) |
| Контроллеры | `POST /preferences/currency` (`[Authorize]`), `POST /preferences/language` (анонимно, ставит culture-cookie), `GET /providers/logo/{id}`, `GET /tools/logo/{id}` (фолбэк — монограмма), `GET /tools/image/{id:guid}`, `GET /admin/providers/logo/{id}`, `POST /admin/gatewaytest/stream` (SSE) |
| Служебные | `POST /webhook/{provider}`, `POST /internal/alerts/grafana`, `GET /health`, `GET /sitemap.xml` (динамический XML-sitemap, `Api/SitemapEndpoint.cs`), OTel-эндпоинты |

Все POST-контроллеры под `[ValidateAntiForgeryToken]`.

**`/models/{provider}/{slug}` — не «id, разрезанный по слэшу».** Публичный id модели этому шаблону
не подчиняется: в каталоге живут и голые id (`claude-opus-4-8`, `GPT-5.5`), и префиксные
(`ORR/Gemini-3.1-Pro`), поэтому склейка `$"{provider}/{slug}"` не даёт существующий `model_id` —
именно так страница детали и отдавала 404 на **каждую** карточку. Контракт зафиксирован в
[`ModelDetailRoute`](Services/Catalog/ModelDetailRoute.cs), и все четыре места (каталог `/models`,
`/providers/{id}`, `sitemap.xml`, сама `ModelDetails`) обязаны ходить через него, а не строить путь
руками:

- **Канонический URL** — `{provider}` = `ModelInfo.ProviderId` (стабильный lowercase-id провайдера,
  не отображаемое имя), `{slug}` = часть id после последнего слэша. Фолбэки сегмента провайдера:
  `ProviderId` → префикс id с единственным слэшем → отображаемое имя; если нет ничего, модель
  не адресуема и ссылка не рендерится (`TryBuildPath` → `null`).
- **Резолв** — сперва `GetModelAsync(slug)` (у большинства моделей id и есть слаг), при промахе —
  скан `ListPublicModelsAsync` через `Matches`. Только каталог знает, какому полному id
  соответствует пара provider+slug: обратно из URL префикс не восстановить.
- **Старые формы 301-редиректятся на канонический URL, а не 404-ят**: отображаемое имя провайдера
  в сегменте (`/models/Antropic/…`), префикс id вместо провайдера (`/models/ORR/…`, так строил
  первый sitemap), полный id в слаге (в т.ч. `%2F`), чужой провайдер при верном слаге.
  Проверка каноничности — `IsCanonical`, ordinal, поэтому отличие в регистре тоже редирект.
- Один слаг = одна модель. Если в каталоге появятся `X` и `ORR/X` одного провайдера, второй
  окажется недостижим (шадоуится первым) — это ограничение контракта, а не баг резолва.
- **Sitemap не строит URL моделей сам** — иначе он снова начнёт публиковать ссылки, с которых
  страница редиректит (или которые 404-ят).

**Detail-страницы каталога отдают настоящий 404, а не soft-404.** `/tools/{id}` и `/providers/{id}`
при отсутствующей или недоступной сущности (в т.ч. черновик tool — публичный `GetTool` возвращает
`null` для `!IsPublished`) выставляют `Response.StatusCode = 404` и рендерят штатный HTML-блок
«не найдено»; страницы пользовательской 404 не имеют, глобального `UseStatusCodePages` нет —
поэтому голый `NotFound()` не используется. Исключение — `ModelDetails`: несуществующая модель
отдаёт пустой `NotFound()` (страница построена на `IActionResult`, ей нужен ещё и 301 на канонический
URL). SEO-следствие: несуществующие URL каталога индексируются не должны, при необходимости — снять
в Search Console.

## Локали и URL (SEO-024)

Публичный сайт **адресует локали путём**: русский под префиксом (`/ru/models`), английский под
префиксом (`/en/models`). Контракт целиком — в
[`Services/Localization/LocaleUrl.cs`](Services/Localization/LocaleUrl.cs); всё, что печатает
публичный URL (canonical, hreflang, sitemap, переключатель языка, tag helper ссылок), обязано
ходить через него, а не склеивать пути руками.

Как это работает и почему так:

- **Префикс переезжает в `PathBase`** ([`LocalePrefixMiddleware`](Services/Localization/LocalePrefixMiddleware.cs)).
  Это ключевое решение: маршруты страниц, `ModelDetailRoute` и подсветка навигации продолжают
  работать с голым путём и про локали ничего не знают, а `UrlHelper`, `LinkGenerator` и
  тильда-пути (`~/app.css`) сами возвращают префикс на место. Ни один `@page` не менялся.
- **`app.UseRouting()` вызван явно и должен таким остаться.** Без явного вызова `WebApplication`
  вставляет свой `UseRouting` в самое начало конвейера — раньше `UseLocalePrefix` — и маршрутизация
  пытается сопоставить ещё не срезанный `/en/models` (или `/ru/models`) с голыми маршрутами: 404 на всём
  дереве. Симптом ровно такой и был при первом прогоне тестов.
- **`UseLocalePrefix` идёт до `UseStaticFiles`** — тильда-ссылки на ассеты приходят уже
  префиксованными (`/en/app.css`, `/ru/app.css`), и без этого статика под `/en` и `/ru` отдавала бы 404.
- **Цепочка культур: `RouteCultureProvider` → `CookieRequestCultureProvider`.**
  URL главнее cookie: пришедший из выдачи на `/en/models` обязан увидеть английский, а на `/ru/models` — русский,
  что бы ни лежало в cookie с прошлого визита. `AcceptLanguageHeaderRequestCultureProvider`
  **удалён намеренно** — с ним один URL отдавал два языка по заголовку, что противоречит
  собственным canonical и hreflang этой страницы. Не возвращать.
- **Приватные разделы (`/workspace`, `/admin`, авторизация) префикса не имеют** и остаются
  cookie-driven: индексируемого URL у них нет, префиксовать нечего.
- **Ссылки в разметке — сырые `href="/..."`, `asp-page` в проекте не используется.** Литеральный
  путь — единственный вид URL, который ASP.NET Core сам `PathBase`'ом не переписывает, поэтому
  ссылки чинит [`LocalePathTagHelper`](TagHelpers/LocalePathTagHelper.cs) (зарегистрирован в
  `Pages/_ViewImports.cshtml`). Он смотрит на `PathBase`, а не на культуру, — ссылки остаются
  в том контуре, где посетитель уже находится. Новые ссылки локализуются автоматически;
  помнить об этом при их добавлении не нужно.
- **`/docs` из контура исключён.** VitePress держит своё двуязычное дерево внутри собственной базы
  (`/docs/` и `/docs/en/`), поэтому `/en/docs*` отдаёт 301 на `/docs/en*`, а `/ru/docs*` — на `/docs/*`, в sitemap `/docs/`
  идёт одной нелокализованной записью, и hreflang на нём layout не печатает — их ставит сам
  VitePress. Иначе кластер документации разъехался бы по двум URL.
- **`x-default` → английский, не русский.** Это версия для тех, чей язык не совпал ни с одной
  нашей; за пределами русскоязычного рынка английский — заведомо лучшая посадка.
- **Дубли и голые пути схлопываются 301-редиректом**: голый `/models` → `/ru/models`, `/` → `/ru`, `/ru/` → `/ru`,
  `/en/` → `/en`, `/EN/...` → `/en/...`, `/RU/...` → `/ru/...`. Редирект канонического нормализатора **обязан** нести `PathBase` — без него любой
  неаккуратный URL молча терял бы префикс.
- Правило зеркальности то же, что у документации: набор альтернатив в hreflang и в sitemap
  обязан быть взаимным и self-referencing на каждой странице кластера. Несимметричный набор
  поисковики отбрасывают целиком, поэтому оба места генерируются из одного `LocaleUrl`.

Что осталось за рамками задачи: английский контент — это перевод русских текстов под русский
спрос. Ранжироваться на новых рынках он сам по себе не будет, для этого нужны отдельные
англоязычные посадочные под свою семантику (переразметка SEO-010/011).

## SEO-метаданные в layout ([_Layout.cshtml](Pages/Shared/_Layout.cshtml))

Общий механизм SEO-метаданных живёт в `_Layout.cshtml` и применяется ко всем страницам
(в т.ч. `_CatalogLayout`/`_WorkspaceLayout`/`_AdminLayout` — они рендерятся внутри общего layout):

- **Self-referencing canonical, отдельный на каждую локаль** — `ViewData["CanonicalUrl"]`, если
  задан; иначе `SiteBaseUrlResolver` base + `Request.PathBase` + `Request.Path` **без query**
  (`/tools?category=x` → `/tools`, UTM не попадают). `PathBase` здесь обязателен: `/en/models`
  канонизируется на себя, а не на `/models` — иначе английское дерево объявлено дублем и
  вылетает из индекса сразу после обхода. Рендерится только на индексируемых страницах (не на noindex).
- **`hreflang` ru/en/x-default** — взаимный self-referencing набор из `LocaleUrl.Alternates`,
  на индексируемых страницах кроме `/docs`. Подробности и причины — в разделе «Локали и URL».
  **`SiteBaseUrlResolver` — только для crawler-метаданных** (canonical, `og:url`, `og:image`,
  `twitter:image`, `<loc>` sitemap): резолвит `PublicSiteBaseUrl` (прод: `https://rawrter.com`)
  с фолбэком на request origin и **никогда не читает `PublicApiBaseUrl`/админскую настройку** —
  иначе в canonical/og/sitemap утёк бы api-поддомен. API-примеры (`base_url` в `_IntegrationExamples`,
  страницы `/docs`) по-прежнему берут адрес из `ApiBaseUrlResolver`.
- **`meta robots="noindex,follow"`** — на приватных/служебных префиксах
  (`/login`, `/signup`, `/forgot-password`, `/reset-password`, `/verify-email`, `/invite/`,
  `/workspace`, `/admin`, `/pay/`, `/error`, `/webhook/`, `/internal/`, `/health`, `/metrics`,
  `/.well-known`) и при `Response.StatusCode >= 400` (страницы каталога с 404 получают noindex,
  canonical и `og:url` при этом не выводятся).
- **`ViewData["NoIndex"] = true`** — точечный отказ от индексации для страницы, которая
  отрезолвилась, но показывать ей нечего. Единственный потребитель — `ProviderDetails` у провайдера
  с нулём включённых моделей: `SitemapEndpoint` такие страницы и так не публикует, а без noindex
  сайт слал бы два противоположных сигнала (в sitemap нет, внутренняя ссылка с `/providers` есть).
- **`meta description`** — `ViewData["MetaDescription"]` (string или IHtmlContent), иначе
  брендовый дефолт `Seo_DefaultDescription` из `_Layout.{ru,en}.resx`. ⚠️ Ключи SEO-дефолта кладутся
  в ресурсы **вьюхи layout**, а не в `SharedResource` — `IViewLocalizer` в layout резолвит ключи
  через `Resources/Pages/Shared/_Layout.*.resx`.
- **Per-page description** — публичные страницы (`/`, `/models`, `/providers`, `/tools`, `/docs`,
  `/support`, `/legal/*`, `/login`, `/signup`) ставят `ViewData["MetaDescription"] = Localizer["MetaDescription"]`
  из своего per-page `{ru,en}.resx`. Detail-страницы строят описание из данных сущности формат-строкой:
  `ModelDetails` — `Localizer["MetaDescription", DisplayName, ProviderTitle, InputPrice, OutputPrice,
  ContextDisplay, PublicModelId]`; `ProviderDetails` — по имени провайдера; `ToolDetails` — по имени и
  tagline (если tagline пуст — фолбэк-ключ `MetaDescriptionNoTagline`). `/login` и `/signup` noindex,
  но описания им тоже задаются. Юридические страницы получили собственные
  `Resources/Pages/Legal/{Privacy,Offer}.{ru,en}.resx` с `Footer_Privacy`/`Footer_Offer`
  (раньше title резолвился как сырой ключ) и `MetaDescription`.
- **Open Graph + Twitter Card** — `og:site_name`, `og:type=website`, `og:title`,
  `og:description`, `og:url` (= canonical), `og:image` и `twitter:card/title/description/image`;
  `og:image`/`twitter:image` = `{baseUrl}/og-image.png` (`wwwroot/og-image.png`, 1200×630 —
  сгенерированный брендовый баннер, правки картинки — через генерацию PNG, не текстом).
- **JSON-LD** — [`Services/Seo/StructuredData.cs`](Services/Seo/StructuredData.cs). Layout сам
  выводит `Organization` + `WebSite`, страницы добавляют свои блоки через
  `ViewData["StructuredData"]` (`IEnumerable<string>` с готовым JSON). Выводится **только на
  индексируемых** страницах. `ModelDetails` — `Product` + `Offer` (цены за токены как
  `UnitPriceSpecification` на 1M), `ModelDetails`/`ProviderDetails` — `BreadcrumbList`.
  Абсолютные URL берутся из **`SiteBaseUrlResolver`**, не из `ApiBaseUrlResolver`: иначе
  структурированные данные разъедутся с canonical и утащат в индекс api-поддомен. Сериализация —
  `System.Text.Json` дефолтным энкодером, он экранирует `<`, `>`, `&`, поэтому недоверенный текст
  каталога внутри `<script type="application/ld+json">` безопасен. CSP не трогали: `script-src`
  уже содержит `'unsafe-inline'`.
- Кириллица в `content` атрибутов кодируется Razor в числовые сущности `&#x...;` — это норм
  (браузер декодирует); при проверках рендера в тестах декодируй `WebUtility.HtmlDecode`.
- **`Context` в Razor Pages не существует** — это свойство layout'ов (`RazorPage`), страницы
  наследуются от `RazorPages.Page`. В `.cshtml` страницы бери запрос через `Model.Request`.

## Перелинковка каталога — обязательное условие индексации

Таблицы каталога кликаются по строке (`onclick="location.href=..."` на `<tr>`), но **поисковик
onclick ссылкой не считает**. Пока имена моделей не были обёрнуты в `<a href>`, все detail-страницы
моделей были сиротами: ни одной входящей ссылки, только sitemap. Инварианты:

- В `Models.cshtml` и `ProviderDetails.cshtml` имя модели — настоящий `<a href>`, путь строит
  **только** `ModelDetailRoute.TryBuildPath`. В `Models.cshtml` провайдер в строке — ссылка на
  `/providers/{ProviderId}`.
- `TryBuildPath` может вернуть `null` (модель не адресуема) — тогда рендерится обычный текст, а
  `onclick`/`tabindex`/`role` **не выводятся вовсе**: Razor опускает атрибут, если его значение
  целиком `null`, иначе получался бы `location.href=''`.
- `ModelDetails` тянет соседние модели того же провайдера (`RelatedModels`, максимум 8, сортировка
  по `Priority`) и рендерит блок ссылок — **единственный** исходящий путь со страницы модели
  в остальной каталог. Ошибка загрузки просто убирает блок, страница живёт.

## Веб-аналитика (Yandex Metrika / GA4) в layout

Внешние счётчики живут в `_Layout.cshtml` и включаются по секции `Analytics`
([`AnalyticsOptions`](Services/Analytics/AnalyticsOptions.cs), env `Analytics__*`):

- **gated по пустым ID**: пустой `GoogleTagId`/`YandexCounterId` → соответствующий сниппет не
  рендерится вообще (dev и тесты остаются чистыми). Теги в `<head>` после OG-блока.
- **CSP расширяется условно** (`Program.cs`, блок security headers): хосты GA добавляются только
  при `GoogleTagId`, хосты Метрики — только при `YandexCounterId`. Без аналитики политика остаётся
  прежней — не «расширять навсегда».
- **GA4 — это не только `www.google-analytics.com`.** В `script-src` идёт `googletagmanager.com`,
  в `img-src` — `www.google-analytics.com`, а в `connect-src` — `googletagmanager.com`,
  **wildcard `*.google-analytics.com`** и **отдельно голый `analytics.google.com`**. Wildcard
  нужен ради региональных collect-endpoint'ов GA4 (`region1..N.google-analytics.com`; какой
  `regionN` выберет gtag.js, определяют настройки свойства/consent, а не IP посетителя), а голый
  родительский домен пишется отдельной записью — **CSP-wildcard матчит только поддомены**.
- **Метрика — это не один `mc.yandex.ru`.** Тег грузится с `mc.yandex.ru`, но дальше сам ходит
  на `mc.yandex.com` (пиксель `sync_cookie_image_check` → `img-src`, транспорт
  `wss://mc.yandex.com/solid.ws` → `connect-src`) и на `mc.webvisor.org`/`mc.webvisor.com`
  (вебвизор); `yastatic.net` отдаёт ассеты вебвизора/кликмапы. Все они перечислены в
  `yandexMetrikaHosts` в `Program.cs` и уходят сразу в `script-src`/`img-src`/`connect-src`.
  **`wss:` пишется отдельной записью** (`yandexMetrikaWssHosts`, только `connect-src`): браузер
  не считает `https://host` покрывающим `wss://host`, и без неё в консоли висит
  «Connecting to 'wss://mc.yandex.com/solid.ws' violates … connect-src».
- **Баннер согласия гейтится по любому счётчику, не только по GA.** Условие в `_Layout` —
  `googleTagId || yandexCounterId`, а `gtag('consent','update',…)` внутри его обработчика обёрнут
  в проверку `googleTagId`. Раньше и то и другое зависело только от GA: при работе одной Метрикой
  баннер не показывался вовсе, `rwt_consent` никогда не выставлялся, и вебвизор с кликмапой
  оставались выключены навсегда. Не «упрощать» обратно.
- **`ecommerce:"dataLayer"` в init убран** — в `dataLayer` никто не пушил, а сама переменная
  существует только внутри GA4-блока. Ценность цели едет параметрами `reachGoal`.
- **Прод-значения продублированы** в `appsettings.json` и `deploy/compose/compose.prod.yml`
  (`Analytics__*`) — это не секреты, они и так уходят в публичный HTML. **В
  `appsettings.Development.json` секция намеренно пустая**: без неё любой локальный `dotnet run`
  льёт хиты и достижения целей в боевой счётчик. Полный runbook подключения —
  `docs/seo/webmasters-analytics-runbook.md`.

### Цели (`Services/Analytics/`)

**Единственный источник истины имён — [`AnalyticsGoals`](Services/Analytics/AnalyticsGoals.cs)**
(константы + `All` + `Macro`). Инлайн-строк имён в коде быть не должно: счётчик матчит имя
буквально, опечатку не поймает ни компилятор, ни отчёт. Раннбук — производное зеркало реестра.
Не путать с `BuildingBlocks/Analytics/AnalyticsEvents` — то внутренний конвейер `analytics.events`,
имена и формы у них разные намеренно.

- **Серверные цели** — `PageModelBase.TrackGoal(...)` / `TrackGoals(...)`, накапливаются
  **списком** в `ViewData[AnalyticsGoal.ViewDataKey]`. Именно накопление, а не одно поле:
  раньше `email_verified` и `api_key_created` стояли под `else if` и были взаимоисключающими.
- **Перенос через редирект** — [`AnalyticsTempData`](Services/Analytics/AnalyticsTempData.cs)
  (`Enqueue`/`Dequeue`, `SetPendingTopUp`/`TakePendingTopUp`). Хранит **строку с JSON**:
  `CookieTempDataProvider` умеет только примитивы и падает на сложном объекте. `Dequeue`/`Take*`
  после чтения ещё и `Remove` — чтение через индексатор снимает ключ для следующего запроса,
  `Remove` для текущего.
- **Ошибочные ветки POST не должны звать GET-хендлер.** В `ApiKeys` загрузка данных вынесена
  в `LoadAsync()`; проход через `OnGetAsync` съедал pending-цель и засчитывал конверсию на
  странице с ошибкой валидации.
- **`topup_completed` — с ценностью.** `OnPostTopUpAsync` кладёт `ProviderAmount`/`ProviderCurrency`
  (реальную сумму списания, не набранную в форме), возврат на `?topup=success` забирает её,
  ставит цель и **редиректит на чистый URL** — это и есть дедупликация по F5. Если cookie не
  доехала, цель ставится **без** суммы: платёж состоялся, терять конверсию нельзя. Admin-only
  чекаут Yandex Pay всегда идёт по этой ветке — это не баг.
- **OAuth-регистрация закрывает `signup` + `email_verified` одним рендером** из
  `ExternalCallback`, по новому полю `LoginResponse.is_new_user` (`auth.proto`, номер 6).
  У внешнего провайдера нет шага подтверждения почты. Линковка провайдера к существующему
  аккаунту новой регистрацией не считается.
- **Клиентские микро-цели — атрибут `data-goal="<имя>"`**, один делегированный обработчик
  в `_Layout`. Он **обязан быть в capture-фазе**: `copyModelId` вызывает `stopPropagation()`,
  и слушатель на всплытии не увидит кликов по кнопкам копирования — самых ценных здесь целей.
  Дедуп в пределах страницы — `dataset.goalFired`.
- **Уводящие ссылки намеренно не задерживаются `preventDefault`** (сломало бы ctrl/средний клик
  и добавило задержку каждому CTA). Клик в первые ~200 мс, пока `tag.js` не приехал, теряется —
  осознанный размен, ни одна **макро**-цель на клик не завязана.
- **Рендер — JSON-остров + статический раннер**: `<script type="application/json"
  id="orr-analytics-goals">` c payload'ом (`AnalyticsGoalScript.BuildPayloadJson`) и скрипт,
  в котором нет ни одного серверного значения. Разведение форматов (`value`/`currency` у GA4 vs
  `order_price`/`currency` у Метрики) живёт **только** в раннере — page model называет сумму один
  раз. Сериализация — `System.Text.Json` дефолтным энкодером (экранирует `<`, `>`, `&`, из
  `<script>` не выйти) и инвариантным `decimal` (под `ru-RU` склейка строк дала бы `1500,50`
  и синтаксическую ошибку JS).
- **`setUserID` — только псевдоним** ([`AnalyticsUserIdProvider`](Services/Analytics/AnalyticsUserIdProvider.cs)),
  только для авторизованных и только при `hasConsent`. Резолвить `UserPseudonymizer` из layout
  напрямую **нельзя** — его фабрика бросает при пустом `ProductAnalytics:IdentitySalt`, а он пуст
  в dev и тестах, так что упали бы все страницы; обёртка возвращает `null`. **Ротация соли рвёт
  связь с уже накопленными профилями** в Метрике и GA4.

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

## Трафика сайта здесь больше не считают

Собственный трекинг визитов удалён: трафик меряет **Яндекс.Метрика** (счётчик в `_Layout.cshtml`,
секция `Analytics`). Вместе с `Services/SiteStats/` ушли `SiteVisitTrackingMiddleware`, ридеры и
админские страницы `/admin/visitors`, `/admin/sessions`, `/admin/sessions/{id}`; таблицы
`analytics.site_visits`/`visit_sessions` дропает `db/migrations/analytics/004_drop_site_visits.sql`,
секция «Посетители сайта» вырезана из дашборда `ORR Users`. Заводить заново не надо — Метрика
покрывает и посетителей, и источники, и UTM. Продуктовая аналитика (`analytics.events`,
`AddProductAnalytics`) — отдельный конвейер, он живой.

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
| `PublicSiteBaseUrl` | канонический сайт для `SiteBaseUrlResolver` (canonical/og/sitemap); пусто → фолбэк на request origin. Прод: `https://rawrter.com` |
| `GrpcServices:{Auth,Billing,ControlPlane,Collector,Content}` | адреса сервисов; `CallDeadlineSeconds` — дедлайн всех gRPC-вызовов |
| `ServiceAuth:{BillingCaller,BillingToken,ContentCaller,ContentToken}` | s2s-креды; **`BillingToken` обязателен**, Content-пара по умолчанию наследует Billing |
| `Google:*`, `Yandex:*` | внешний логин; провайдер не регистрируется, пока не заданы оба значения |
| `Email:*` | транспорт, SMTP-хост/порт/SSL, отправитель, `BatchDelayMs`; секреты (`BrevoApiKey`, SMTP-логин/пароль) — только env/secret |
| `Support:{Inbox,TelegramUrl}` | получатель формы `/support` и альтернативный канал |
| `ProductAnalytics:*` | синк `orr_analytics` (`Enabled`, `ConnectionString`, `IdentitySalt`, `Environment`, размеры очереди) |
| `Analytics:{GoogleTagId,YandexCounterId}` | теги веб-аналитики в layout; пусто → сниппет не рендерится, CSP остаётся строгим |
| `ExchangeRates:*` | display-only курсы ЦБ РФ + фолбэки |
| `Payments:*` | `ActiveProvider`, `Min/MaxTopUpUsd`, `Mock.SharedSecret`, `Rollypay.*`, `YandexPay.*` (Enabled, Sandbox, MerchantId, SuccessRedirectUrl, FailRedirectUrl, OrderTtlSeconds), `Reconciliation.*` |
| `Alerting:{WebhookToken,EmailTo}` | алерт-вебхук; **в `appsettings.json` отсутствуют** |

`appsettings.Development.json` включает mock-провайдер, dev s2s-токен и dev-креды Yandex —
в прод это не уезжает (`PaymentStartupGuard` + отдельные секреты).

## Подводные камни

- **Только gRPC/HTTP-клиенты** — доменной БД у WebApp нет, вся логика в backend-сервисах.
  Единственное прямое подключение к Postgres — аналитический синк (`analytics.events`);
  для доменных данных так делать нельзя.
- **Новая строка UI = две `.resx`** (`.ru.resx` + `.en.resx`) в пути, зеркалящем путь страницы,
  иначе на второй культуре останется ключ. Новый маршрут — не забыть ссылку в соответствующем
  layout'е и что кебаб-кейс сам не появится (нужна директива `@page "..."`).
- **Английская версия адресуема префиксом `/en` (SEO-024).** Русский живёт на голом пути
  (`/models`), английский — под `/en/models`. Русский намеренно **без** префикса: все уже
  проиндексированные URL сохранили адрес, так что английский контур не стоил ни одного редиректа
  на стороне, которая приносит трафик. Как это устроено — см. раздел «Локали и URL» ниже.
  Разворачивать обратно на cookie-локализацию нельзя: это ровно то состояние, из-за которого
  EN-контент был непроиндексируем (Googlebot краулит с US-IP и `Accept-Language` не шлёт).
- **У основной auth-cookie и cookie TempData явно задан `Path=/`.** Не убирать: на английском URL
  `LocalePrefixMiddleware` переносит `/en` в `Request.PathBase`, а провайдеры кук без
  явного пути используют текущий `PathBase`. Тогда `/en/logout` удаляет несуществующую cookie с `Path=/en`,
  оставляя реальную корневую сессию активной, а `TempData` (созданный ключ API `NewKeyValue`, цели аналитики)
  не доезжает между запросами.
- **Юридические документы живут только в WebApp.** `/legal/privacy` и `/legal/offer` рендерит
  `LegalDocumentService` из `docs/mandatory/*.md` с подстановкой плейсхолдеров `ORR_*`. Копии
  в `docs-site/legal/` удалены: они отставали по содержанию (не было пунктов про «Яндекс Пэй»),
  дублировали контент на втором URL и показывали сырые `{{ORR_SERVICE_NAME}}` — подстановки
  в VitePress нет. Меню документации ведёт абсолютными ссылками на `/legal/*`. Не заводить заново.
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
  запроса. Ошибка Billing не валит страницу, просто уводит на фолбэк. Используется только для
  **API-примеров** (`base_url`, `/docs`) — не для SEO. Для crawler-метаданных отдельный
  `SiteBaseUrlResolver` (`PublicSiteBaseUrl` → origin), чтобы api-поддомен не утекал в
  canonical/og/sitemap.
- `ReasoningEnrichmentWorker` крутится **в каждой реплике**, идемпотентен по `model_id` и **не
  перетирает** админскую разметку reasoning-возможностей (патчит только не отмеченные модели или
  отмеченные с пустым списком efforts).
- `UseForwardedHeaders` должен остаться первым middleware, иначе за Caddy ломаются OAuth и
  абсолютные ссылки. `ForwardLimit = 1` ограничивает обработку ближайшим Caddy hop, чтобы
  подставленный клиентом `X-Forwarded-For` не обходил per-IP rate limit платёжного вебхука;
  не фиксировать для этого Docker-подсеть и не убирать forwarding схемы/host.
- Внешний логин не появится, пока не заданы оба секрета соответствующего провайдера.
- **`<img src="/providers/logo/{id}">` можно рендерить безусловно** — `PublicProviderLogoController` сам отдаёт либо загруженный лого (`LogoBytes`), либо сгенерированную SVG-монограмму. Не дублируйте на вызывающей стороне проверку «`HasLogo`?» перед вставкой тега: это ровно тот баг, из-за которого на `/models` провайдеры без загруженного лого (DeepSeek, Z.ai, MiniMax, …) показывали уродливые текстовые инициалы, а на `/providers/{id}` и `/` — нормальный бренд-бейдж. Проверка нужна только там, где по факту отсутствия лого **меняется поведение** (например, фильтр-кнопка в шапке каталога рендерит инициал вместо картинки, чтобы не возиться со стилями).
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
  markdown, курсы ЦБ, activity, support, страницы Tools,
  SEO-заголовки layout (`SeoHeadTests` — canonical без query, noindex на /login и /signup,
  description и OG/Twitter-теги).
- **E2E** — Playwright, opt-in (`tests/OpenRussiaRouter.E2ETests`). База —
  `ORR_E2E_BASE_URL` / `e2e.local.json` (дефолт `http://localhost:5004`), креды —
  `ORR_E2E_USER_EMAIL`/`ORR_E2E_USER_PASSWORD`; **без кредов тесты тихо скипаются**.
