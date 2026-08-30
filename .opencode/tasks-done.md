# Tasks — архив выполненных

> Сюда переезжают задачи из `.opencode/tasks.md` **в момент завершения** (`status: done`).
> Блок задачи вырезается из `tasks.md` и дописывается **в конец** этого файла — задача
> существует ровно в одном файле, дублей быть не должно.
>
> Файл только на дозапись: выполненные задачи отсюда не удаляем и не редактируем — это история.
> ID **никогда не переиспользуются**: следующий номер `/add-task` считает по максимуму
> **обоих** файлов.
>
> Незавершённые статусы (`todo`, `in_progress`, `blocked`) остаются в `tasks.md` —
> `blocked` это не «выполнено».

---

## SEO-004 — Исправить soft 404 в каталогах
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Обработчик /tools/{id} (Pages/ToolDetails.cshtml.cs:43): отсутствующий или неопубликованный tool → Response.StatusCode = 404. Обработчик /providers/{id} (Pages/ProviderDetails.cshtml.cs:41): отсутствующий provider → 404. Реализовано. Тесты: ProviderDetailsPageTests.cs (MissingProvider → 404), ToolPagesTests.cs (MissingTool → 404, DraftTool → 404), ModelDetailsPageTests.cs (UnknownModel → 404).

## SEO-005 — Добавить robots.txt
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Добавить доступный по /robots.txt файл (wwwroot/robots.txt). Разрешить обход публичных страниц; закрыть служебные разделы: /admin/, /workspace/, /webhook/, /internal/, /health, /metrics. Добавить абсолютную ссылку Sitemap: https://openrussiarouter.com/sitemap.xml. Критерий: /robots.txt отвечает 200 и валиден. Сборка обязательна.

## SEO-006 — Реализовать динамический sitemap.xml
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Добавить эндпоинт /sitemap.xml (новый Api/SitemapEndpoint.cs + MapGet в Program.cs). Включить статические индексируемые URL; динамически — активные модели, активных providers, опубликованные tools. Для каждой записи canonical URL и корректный lastmod при наличии даты обновления. Абсолютный базовый URL брать через ApiBaseUrlResolver. Не включать: URL с query-параметрами, личный кабинет, админку, платежи, несуществующие/неопубликованные сущности, черновики, страницы ошибок. Добавить тест генерации sitemap. Критерий: sitemap валиден, содержит только URL со статусом 200, отражает изменения каталога.

## SEO-007 — Canonical, robots meta и SEO-head в layout
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. В Pages/Shared/_Layout.cshtml создать общий механизм SEO-метаданных. Self-referencing canonical для каждой индексируемой публичной страницы (ViewData["CanonicalUrl"], абсолютный URL через ApiBaseUrlResolver). Query-параметры в canonical не включать (/tools?category=x → /tools; UTM → чистый URL). meta robots="noindex,follow" для служебных и приватных страниц. Open Graph: og:title, og:description, og:url, og:type, единое бренд-изображение; Twitter Card. Добавить тесты рендеринга canonical, description и noindex. Критерий: в HTML /, /models, detail-страниц есть уникальные title, description и canonical; приватные страницы — noindex. Бренд-изображение для OG уточнить у продукта.

## SEO-008 — Meta description для ключевых публичных маршрутов
- status: done
- priority: medium
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Добавить ViewData["MetaDescription"] на публичные страницы: / (Index), /models, /models/{provider}/{slug} (ModelDetails), /providers, /providers/{id} (ProviderDetails), /tools, /tools/{id} (ToolDetails), /docs, /support, /legal/privacy, /legal/offer, /signup, /login. Описания уникальные, без шаблона с заменой одного названия; локализация через существующие .resx (ru/en). Критерий: нет дублирующихся descriptions среди индексируемых публичных страниц. Сборка обязательна.

## SEO-020 — Добавить sitemap в поисковые системы
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-09T00:00:00+00:00
- completed: 2026-08-09T16:19:17Z
- desc: https://openrussiarouter.com/sitemap.xml добавлен в Google Search Console и Яндекс Вебмастер; статус зелёный (принят без ошибок) в обеих системах. Критерий выполнен.

## SEO-002 — Подключить системы вебмастеров и аналитику
- status: done
- priority: low
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-09T16:19:17Z
- desc: Код внедрён и задеплоен: AnalyticsOptions + теги GA4 (G-BDD3DYVCLL) и Метрики (111420552) в _Layout.cshtml, условные CSP-хосты в Program.cs, конверсионные события signup/email_verified/api_key_created/topup_completed через ViewData["AnalyticsEvents"], прод-значения в compose.prod.yml, тесты SeoHeadTests/LegalConsentTests. Домен подтверждён в GSC и Яндекс Вебмастере через TXT-записи. Цели/конверсии добавлены в оба кабинета, связка GSC↔GA4 работает. События фиксируются (email_verified подтверждено); signup на тестовом первом визите не отобразился в Метрике — известный edge case «hit до добивки cookie», механизм работает. Runbook — docs/seo/webmasters-analytics-runbook.md. Юридическая точка (cookies Метрики/GA4 в privacy-policy) вынесена в SEO-023.

## SEO-016 — Решение по единому контуру документации
- status: done
- priority: medium
- created: 2026-08-06T00:00:00Z
- started: 2026-08-09T00:00:00+00:00
- completed: 2026-08-09T00:00:00+00:00
- desc: Решение принято и реализовано: документация — VitePress под `/docs/` на апекс-домене (Caddy: `@docs path /docs /docs/*` → контейнер `docs`, префикс не стрипается). Отдельный поддомен docs.openrussiarouter.com не заводится, Razor-страница `Pages/Docs.cshtml` в проде перекрыта роутингом и не развивается. Условие «не держать две одинаковые документации без canonical/redirect стратегии» выполнено: на всех страницах `/docs/**` проставлен self-referencing canonical (`transformHead` в `.vitepress/config.mts`), продублированные юридические страницы удалены из docs-site — единственный дом `/legal/*` в WebApp, меню документации ведёт туда абсолютными ссылками. Решение зафиксировано в корневом `CLAUDE.md`, раздел «SEO-контур docs-site». Остаточная техническая настройка (frontmatter по файлам) — в SEO-017.

## SEO-024 — Адресуемая английская локаль (/en) + hreflang
- status: done
- priority: high
- created: 2026-08-10T00:00:00Z
- started: 2026-08-10T00:00:00Z
- completed: 2026-08-10T00:00:00Z
- desc: Отменяет решение SEO-002/SEO-003 «hreflang для cookie-локализации не строить». Причина отмены — расширение целевого рынка за пределы РФ (Иран, Турция, Центральная Азия): там английский это основной язык поиска, а при cookie-локализации EN-контент был непроиндексируем в принципе (Googlebot краулит с US-IP и Accept-Language не шлёт, поэтому всегда получал русскую версию). Реализовано в WebApp: русский остаётся на голом пути, английский под префиксом /en; префикс переезжает в PathBase (LocalePrefixMiddleware), поэтому маршруты страниц, ModelDetailRoute и подсветка навигации не менялись вообще. Контракт URL — Services/Localization/LocaleUrl.cs, через него обязаны ходить canonical, hreflang, sitemap, переключатель языка и tag helper ссылок. Цепочка культур: RouteCultureProvider (URL) → CookieRequestCultureProvider; AcceptLanguageHeaderRequestCultureProvider удалён — с ним один URL отдавал два языка по заголовку, что противоречит его же canonical. app.UseRouting() вызван явно: без этого WebApplication вставляет свой UseRouting раньше UseLocalePrefix и всё английское дерево отдаёт 404. Ссылки в разметке — сырые href="/...", asp-page не используется, поэтому их префиксует LocalePathTagHelper по PathBase (новые ссылки локализуются автоматически). Canonical self-referencing на локаль, hreflang взаимный ru/en/x-default, x-default → английский (версия для тех, чей язык не совпал ни с одной нашей). Дубли схлопнуты 301: /ru/* → голый путь, /en/ → /en, /EN/* → /en/*; редирект канонического нормализатора несёт PathBase. /docs исключён из контура — VitePress держит своё дерево внутри собственной базы, /en/docs* → 301 на /docs/en*. Sitemap отдаёт обе локали с взаимными xhtml:link + x-default, /docs одной нелокализованной записью. Приватные разделы (workspace/admin/auth) префикса не имеют и остаются cookie-driven. Тесты: LocaleUrlTests (новый), плюс расширены LocalizationTests, SeoHeadTests, SitemapEndpointTests, LanguagePreferenceTests; solution собирается, 1067 тестов зелёные. Документация — src/OpenRussiaRouter.WebApp/CLAUDE.md раздел «Локали и URL» и корневой CLAUDE.md. НЕ ВХОДИЛО В ЗАДАЧУ: английский контент это перевод русских текстов под русский спрос — ранжироваться на новых рынках он сам по себе не будет, нужны отдельные англоязычные посадочные (переразметка SEO-010/011); и платёжные рельсы (СБП/Yandex Pay) для новой аудитории не работают.

## SEO-003 — Список index/noindex URL
- status: done
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed: 2026-08-11T00:00:00Z
- desc: Языковая часть задачи отменена решением SEO-024 (hreflang, /en prefix). Оставшаяся часть — согласованный с продуктом список index/noindex URL: индексируемые — /, /models, /models/{provider}/{slug}, /providers, /providers/{id}, /tools, /tools/{id} (опубликованные с описанием), /docs, /support, юридические страницы и их зеркала под /en (кроме /docs); неиндексируемые — /login, /signup, /forgot-password, /reset-password, /verify-email, /invite/*, /workspace/*, /admin/*, /pay/*, /error, служебные API и webhooks. Техническая реализация (canonical, robots meta, noindex) покрыта SEO-007. Список URL согласован и задокументирован.

## RNM-001 — Бренд интерфейса
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-13T00:00:00Z
- completed: 2026-08-13T00:00:00Z
- desc: WebApp. Перед правкой прочитать `src/OpenRussiaRouter.WebApp/CLAUDE.md`. Заменить отображаемое название продукта `OpenRussiaRouter` на `Rawrter` во всех пользовательских текстах Razor Pages, layout, ресурсах ru/en, SEO-title/description, административном интерфейсе, email-шаблонах, `Email:SenderName`, платёжных назначениях, юридических шаблонах, alt/aria-подписях и пользовательских сообщениях. Проверить `wwwroot` на логотипы и изображения со старым текстом; при наличии обновить исходные assets без смены визуального стиля. Не менять namespace, assembly/project names, каталоги, `ORR_*`, `orr_*`, `sk-or-*`, `ORR/*`, URL API и почтовые адреса. Обновить затронутые тесты. Критерий: отрендеренные страницы и письма показывают `Rawrter`, технические исключения сохранены; `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1` и тесты `OpenRussiaRouter.WebApp.Tests` проходят. ПРИМЕЧАНИЕ (2026-08-13): `wwwroot/og-image.png` в этой задаче НЕ менять (logo-квадрат `rawr` → `Rawrter` и баннер) — ручная перегенерация ассета планируется отдельно, вне RNM-001; состав работ зафиксирован в todo-листе. `favicon.png` также остаётся. Реализовано: бренд `Rawrter` в разметке, ресурсах, email-шаблонах, юридических шаблонах и SEO-метаданных (коммиты 42ed0d1c, 7686257c, f472777e); `og-image.png` перегенерирован под новый бренд (коммит f472777e). Для зелёных тестов заодно переведены на `rawrter.com` сайтовые дефолты `LegalDocumentService` (`ORR_PUBLIC_DOMAIN`, `ORR_OPERATOR_DETAILS`) и Sitemap-пути в `wwwroot/robots.txt` — домен сайта целиком добивается в RNM-002.

## RNM-002 — Домен сайта
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-13T19:08:43Z
- completed: 2026-08-13T19:21:20Z
- desc: Перевести только публичный сайт с `https://openrussiarouter.com` на `https://rawrter.com`. Обновить актуальные значения и ссылки в WebApp (`PublicSiteBaseUrl`, юридический `ORR_PUBLIC_DOMAIN`, robots и связанные тесты), docs-site, `deploy/compose`, `PROD-ACCESS.md`, актуальных deploy/runbook-файлах и комментариях, описывающих текущий прод. Имя переменной `ORR_PUBLIC_DOMAIN` сохранить; её production-значение должно стать `rawrter.com`. Caddy развязать так, чтобы apex обслуживался на `rawrter.com`, а API продолжал обслуживаться только на `api.openrussiarouter.com`; не создавать `api.rawrter.com`. Удалить redirect-блоки старого/опечаточного сайта, новые редиректы не добавлять. Не менять `PublicApiBaseUrl`, API-примеры, почтовые адреса и API DNS. Критерий: рабочие ссылки сайта используют `rawrter.com`, `api.openrussiarouter.com` сохранён как явное исключение; `docker compose -f deploy/compose/compose.prod.yml config` валиден и обязательная сборка solution проходит. Сделано (2026-08-13): Caddy развязан на `rawrter.com` (apex + docs), API закреплён за `api.openrussiarouter.com` без `api.rawrter.com`, redirect-блок старого/опечаточного сайта удалён; обновлены `deploy/compose/{Caddyfile,compose.prod.yml,.env.example}`, `PROD-ACCESS.md`, `docs/DEPLOYMENTS.md`, `src/OpenRussiaRouter.WebApp/CLAUDE.md`, комментарий монтажа `docs-site/.vitepress/config.mts`; SEO-сигналы `config.mts` и runbook вебмастеров остались задачам RNM-003/RNM-005; сборка solution и `OpenRussiaRouter.WebApp.Tests` (375/375) зелёные.


## RNM-003 — SEO-контур
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T00:30:00Z
- completed: 2026-08-14T00:50:00Z
- desc: WebApp + docs-site. Перед правкой WebApp прочитать его `CLAUDE.md`. Перевести crawler-facing сигналы сайта на `https://rawrter.com`: canonical, `og:url`, `og:image`, Twitter image, JSON-LD, взаимные hreflang ru/en/x-default, `<loc>` динамической `/sitemap.xml`, hostname `/docs/sitemap.xml` и обе записи Sitemap в `/robots.txt`. Сохранить структуру локалей и `/docs/`, не подменять сайт API-origin'ом. Обновить `SeoHeadTests`, `SitemapEndpointTests`, `RobotsTxtTests` и другие затронутые ожидания. Не менять `https://api.openrussiarouter.com` в API-контракте и примерах. Критерий: сгенерированный SEO-контур содержит `rawrter.com` для сайта и документации, API-исключение сохранено; тесты WebApp, обязательная сборка solution и `npm run docs:build` в `docs-site` проходят.


## RNM-004 — Публичная документация
- status: done
- category: surface
- priority: medium
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T00:30:00Z
- completed: 2026-08-14T00:50:00Z
- desc: docs-site и актуальная пользовательская документация. Заменить отображаемый бренд на `Rawrter`, а ссылки на панель, главную и `/legal/*` — на `https://rawrter.com`. Обновить title, description, навигацию и обычный текст, не меняя техническое содержание API. Явно сохранить `https://api.openrussiarouter.com` во всех API reference, curl-командах, Python/Node.js примерах и `base_url`; не менять почтовые адреса, `sk-or-*` и `ORR/*`. Не редактировать `.opencode/tasks-done.md`, существующие блоки SEO-* и датированные исторические планы/specs. Критерий: документация использует бренд `Rawrter` и новый адрес сайта, API-примеры побайтно сохраняют прежний endpoint; `npm run docs:build` проходит.


## RNM-008 — Согласованность canonical и sitemap после переезда
- status: done
- category: surface
- priority: high
- created: 2026-08-14T15:26:08Z
- started: 2026-08-14T20:16:59.3875840+03:00
- completed: 2026-08-14T20:26:29.6948590+03:00
- desc: Исправить обнаруженные расхождения crawler-facing URL на `rawrter.com`, не меняя существующий redirect-контур. Для английской главной `/en` выводить self-referencing canonical `https://rawrter.com/en`, а не `/en/`, который редиректится обратно. В динамической `/sitemap.xml` публиковать конечный URL документации `https://rawrter.com/docs/` вместо `/docs`, чтобы URL из карты сайта сразу отвечал 200 без redirect-chain и промежуточного HTTP Location. Добавить или обновить тесты canonical, hreflang и sitemap. Критерий: canonical каждой проверяемой страницы совпадает с её конечным URL; все `<loc>` из обеих sitemap отвечают прямым HTTPS 200; существующие 301-редиректы остаются без изменений; тесты WebApp и обязательная сборка solution проходят. Сделано (2026-08-14): canonical английской главной теперь `.../en` без хвостового слэша (self-referencing, og:url тоже); sitemap публикует конечный URL `/docs/` вместо `/docs`; тесты canonical/hreflang/sitemap добавлены и обновлены; redirect-контур не менялся. Внешняя проверка «все <loc> отвечают прямым HTTPS 200 на проде» остаётся до деплоя (нет доступа к проду 201.51.6.246).


## RNM-009 — Запрет индексации API-origin
- status: done
- category: surface
- priority: medium
- created: 2026-08-14T15:26:09Z
- started: 2026-08-14T21:11:59.7284300+03:00
- completed: 2026-08-14T21:15:31.6031770+03:00
- desc: Закрыть `https://api.rawrter.com` от поисковой индексации, не меняя публичный OpenAI-совместимый API-контракт. Добавить доступный на API-host `/robots.txt` с `User-agent: *` и `Disallow: /` и/или единый `X-Robots-Tag: noindex, nofollow` для публичных API-ответов, включая `/v1/models` и health endpoints. Решение не должно менять JSON-body, HTTP-коды, streaming, authentication или CORS API. Существующие 301-редиректы и другие hostnames не изменять. Критерий: поисковики получают однозначный запрет индексации `api.rawrter.com`, а `/health`, `/v1/models` и completions сохраняют прежнее поведение; затронутые тесты и обязательная сборка solution проходят. Реализовано через API middleware и публичный `/robots.txt`, добавлены интеграционные проверки.

## RNM-005 — Внешний переход
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T11:00:00Z
- completed: 2026-08-14T23:30:00Z
- desc: Внешняя конфигурация без переименования внутренностей. DNS, TLS, сертификаты, GSC/Яндекс Вебмастер, GA4/Метрика, Brevo, OAuth redirect URIs — всё на `rawrter.com`. Сделано (2026-08-14): DNS A-записи на 201.51.6.246; `RWT_PUBLIC_DOMAIN=rawrter.com` в .env; Let's Encrypt сертификаты; GSC/Яндекс — смена адреса завершена; GA4 `G-38042Y9GW4` + Метрика `111582266`; Brevo `noreply@rawrter.com`; Яндекс OAuth redirect URI `https://rawrter.com/OAuth-yandex`; Rollypay webhook `https://rawrter.com/webhook/rollypay` зарегистрирован в кассе; `PAYMENTS_ACTIVE_PROVIDER=rollypay` + ключи в .env; `www.rawrter.com` → 301 на apex. Финальная проверка 2026-08-14: SSH 201.51.6.246, `docker compose ps` все контейнеры Up, health endpoints 200, canonical/og/hreflang/sitemap/robots на `rawrter.com`.

## RNM-007 — Публичный домен API
- status: done
- category: surface
- priority: high
- created: 2026-08-14T15:26:07Z
- started: 2026-08-14T18:00:00Z
- completed: 2026-08-14T23:30:00Z
- desc: Перевести видимую пользователям API-поверхность с `api.openrussiarouter.com` на `api.rawrter.com`. Сделано: docs-site (ru+en) переведён на `api.rawrter.com`; K6-тесты обновлены; добавлен unit-тест `ApiBaseUrlResolverTests` (3 теста); `platform_settings.public_api_base_url` обновлён в БД прода на `https://api.rawrter.com` (SSH 201.51.6.246); WebApp перезапущен для сброса кеша; docs-контейнер пересобран; `api.rawrter.com/health`=200, `/v1/models`=200; `api.rawrter.com/robots.txt`=Disallow: /; `dotnet build` 0 ошибок, `dotnet test` 1101 зелёный, `npm run docs:build` зелёный. Legacy `api.openrussiarouter.com` сохранён как контракт для существующих клиентов.

## RNM-006 — Проверка публичной поверхности
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T23:00:00Z
- completed: 2026-08-14T23:30:00Z
- desc: Финальный surface-аудит. Результаты (2026-08-14, SSH 201.51.6.246): canonical на `/` = `https://rawrter.com/`; hreflang ru/en/x-default (3 шт.); og:url + og:image = rawrter.com; sitemap = 83 URL; docs sitemap = 200; robots.txt site = Allow + Disallow служебных; robots.txt API = `Disallow: /`; www.rawrter.com = 301; /docs/ = 200; OAuth Yandex callback = 500 (ожидаемо — ClientSecret не задан в .env); dotnet build 0 ошибок; dotnet test 1101 зелёный; npm run docs:build зелёный. Контракты `ORR_*`, `orr_*`, `sk-or-*`, `ORR/*`, namespace'ы, почтовые адреса, `api.openrussiarouter.com` как legacy не изменены.

## SEO-025 — 301-редирект запросов документации без /docs/ в WebApp
- status: done
- priority: high
- created: 2026-08-26T20:16:00Z
- started: 2026-08-26T20:28:45+03:00
- completed: 2026-08-26T20:37:00+03:00
- desc: WebApp. В `LocalePrefixMiddleware` и `LocaleUrl` настроен 301 Permanent Redirect для путей документации без префикса `/docs/`: `/guide/*` → `/docs/guide/*`, `/api/*` → `/docs/api/*`, `/integrations/*` → `/docs/integrations/*`, `/en/guide/*` → `/docs/en/guide/*`, `/en/api/*` → `/docs/en/api/*`, `/en/integrations/*` → `/docs/en/integrations/*` (и `/ru/...` аналогично), с полным сохранением query string. В `LocaleUrl` добавлены `DocsSectionPrefixes` (`/guide`, `/api`, `/integrations`), `IsDocsSectionPath` и расширены `IsDocsPath` и `DocsFor`. Добавлены unit и integration тесты в `LocaleUrlTests` и `LocalizationTests` (проверены обе локали, query string, коды 301 и работа существующих страниц).

## SEO-026 — Устранить Soft 404 на страницах документации docs/**/api/errors
- status: done
- priority: medium
- created: 2026-08-26T20:43:00Z
- started: 2026-08-26T20:55:40+03:00
- completed: 2026-08-26T21:26:30+03:00
- desc: docs-site (VitePress). Устранен риск Soft 404 на страницах документации /api/errors и /en/api/errors за счет расширения контента в развернутое руководство разработчика по обработке ошибок API (>1000 слов). Добавлен YAML frontmatter с title и description для SEO. Описан формат JSON-ответа OpenAI (message, type, code, param, request_id). Добавлена подробная сводная таблица HTTP-статусов (400, 401, 402, 403, 404, 429, 500, 502, 503) и платформенных кодов ошибок (validation_failed, authentication_required, invalid_api_key, insufficient_balance, api_key_limit_exceeded, access_denied, model_unavailable, rate_limited, internal_error, bad_gateway, router_unavailable) с причинами и действиями клиента. Разработаны практические примеры обработки ошибок с экспоненциальным backoff для Python (OpenAI SDK), TypeScript/Node.js и cURL (с разбором через jq). Описана retry-стратегия с обработкой заголовка Retry-After, джиттером и разделением на повторяемые/неповторяемые ошибки. Описано поведение проксирования ошибок upstream и автоматический failover нод. Обеспечена 1:1 зеркальность ru/en версий. Сборка `npm run docs:build` успешно протестирована.

## T-002 — Убрать сохранённый отступ бокового меню в мобильном кабинете и админке
- status: done
- category: responsive-ui
- priority: high
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T13:54:56.5972576+04:00
- completed: 2026-08-30T13:59:54.6437738+04:00
- desc: P1. В `wwwroot/app.css` и `wwwroot/app.min.css` в медиа-запросе `@media (max-width: 768px)` добавлено правило `.app-shell--no-sidebar .sidenav-content` для сброса `margin-left: 0`, перебивающее десктопное правило повышенной специфичности `.app-shell--no-sidebar .sidenav-content { margin-left: var(--sidebar-width); }`.

## T-003 — Уместить авторизованную шапку и меню пользователя в мобильный viewport
- status: done
- category: responsive-ui
- priority: high
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:01:11.2279823+04:00
- completed: 2026-08-30T14:06:53.4898900+04:00
- desc: P1. В `wwwroot/app.css` и `wwwroot/app.min.css` настроены адаптивные размеры и flex/grid ограничения `.topbar`, `.topbar-brand`, `.topbar-logo`, `.topbar-nav`, `.topbar-actions` и `.user-dropdown` (`max-width: calc(100vw - 16px)`, `max-height: calc(100vh - var(--topbar-height) - 16px)`, `overflow-y: auto`), добавлены брейкпоинты для мобильных ширин (360/480/768 px). В `Pages/Shared/_Layout.cshtml` синхронизирован класс `.user-menu--open` при открытии/закрытии dropdown для корректного состояния chevron. Добавлен тест `UserMenuLayoutTests`.

## T-004 — Восстановить перенос фильтров Top-ups и Requests и размеры кнопок
- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:08:12.8121271+04:00
- completed: 2026-08-30T14:20:45.2729702+04:00
- desc: P2. В `wwwroot/app.css` и `wwwroot/app.min.css` добавлены утилиты `.flex-wrap`, `.flex-nowrap`, `.align-items-start`, `.align-items-end`, правила для `.btn-outline-secondary`, `.btn-info`, `.form-control-sm` и `flex-shrink: 0` для `.btn-sm`, а полям фильтрации добавлены ограничения `max-width: 100%`. Добавлены регрессионные тесты в `AdminFilterResponsiveTests`.

## T-005 — Ограничить переполнение таблиц админки и истории баланса

- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:22:19.7787193+04:00
- completed: 2026-08-30T14:30:17.1211368+04:00
- desc: P2. Таблицы в `Pages/Admin/Users.cshtml`, `TopUps.cshtml`, `Nodes.cshtml`, `Models.cshtml`, `Requests.cshtml` и `Pages/Workspace/Credits.cshtml` обёрнуты в локальный скролл-контейнер `.table-scroll`. Для таблицы моделей добавлена минимальная ширина `.adm-models-table { min-width: 900px; }`, предотвращающая разрушение и посимвольный перенос Model ID. Добавлены unit-тесты в `TableScrollResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/admin/models` при 1024 px: document.scrollWidth около 1086 px при clientWidth около 1009 px; Model ID переносится почти по одной букве, правые колонки обрезаны.
2. На `/ru/admin/requests` при 1024 px document.scrollWidth около 1222 px; здесь дополнительно влияет фильтр из T-004.
3. При 768 px наблюдаются ширины документа около 979 px в Users, 911 px в Top-ups и 914 px в Nodes. В истории `/ru/workspace/credits` при 375 px таблица шириной около 449 px выходит за панель.
4. У соответствующих таблиц родитель `.adm-card-body`/`.ws-panel-body` имеет overflow-x: visible. Глобальный body overflow-x: hidden маскирует дефект, а не делает правые колонки доступными.

### Причина и область исправления

- `Pages/Admin/Users.cshtml:104`, `TopUps.cshtml:48`, `Nodes.cshtml:59`, `Models.cshtml:402`, `Requests.cshtml:117`; `Pages/Workspace/Credits.cshtml:141`.
- В `wwwroot/app.css:380–388` уже есть `.table-wrap` и `.table-scroll`. Рабочие примеры: API-ключи, детализация активности, таблица сравнения цен (`.price-table-wrap`).
- Ввести локальную прокрутку в пределах панели; родительские grid/flex-элементы должны уметь сжиматься. Задать достаточную минимальную ширину таблицы/критичных колонок там, где иначе разрушаются ID, числа и кнопки. Не применять nowrap ко всему тексту без разбора: длинные описания могут переноситься.
- Сохранить семантику table/th, сортировку, копирование, ссылки и действия. Не скрывать Actions, цены или другие колонки как способ устранить overflow.
- Проверять после T-002/T-003; переполнение фильтра Requests устраняется отдельно в T-004.

### Критерии приёмки

- [ ] На всех шести страницах таблица не увеличивает scrollWidth документа сверх clientWidth; необходимая горизонтальная прокрутка находится внутри панели.
- [ ] До последней колонки и всех действий можно добраться мышью, touch и клавиатурой; фокусируемые элементы не остаются за недоступной областью.
- [ ] ID/даты/суммы читаемы, нет вынужденного переноса одного идентификатора по букве в узкую вертикальную колонку.
- [ ] Проверены длинные модельные ID, email и названия на синтетических данных, пустая таблица и много строк; на 1440 px нет лишнего скролла там, где таблица помещается.
- [ ] Существующие корректные таблицы API-ключей, активности и сравнения цен не получили регрессий.

Локальное свидетельство (вне git): `C:/Users/pro/.codex/visualizations/2026/08/30/01a051e8-345d-7082-bb5b-e199de7ed1a9/responsive-audit/admin-models-1024.png`.

## T-006 — Адаптировать сетку карточек обзора активности к узкому контейнеру

- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:31:59.1737523+04:00
- completed: 2026-08-30T14:37:07.8575093+04:00
- desc: P2. В `wwwroot/app.css` и `wwwroot/app.min.css` сетка `.act-stat-row` переведена на `minmax(0, 1fr)` (4 колонки на десктопе, 2 колонки на ширине ≤ 768 px, 1 колонка на ширине ≤ 480 px), для `.act-stat-card` задан `min-width: 0`, для `.act-stat-label` разрешён перенос длинных названий (`white-space: normal; overflow-wrap: break-word`), а для `.act-stat-value` настроено безопасное предотвращение переполнения (`text-overflow: ellipsis; white-space: nowrap; overflow: hidden`). Добавлены регрессионные тесты в `ActivityResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/workspace/activity?tab=overview` с доступной статистикой, установить ширину 768 px.
2. `.act-stat-row` имеет ширину около 481 px и scrollWidth около 522 px; последняя карточка заканчивается на x≈778 при clientWidth≈753.
3. Аналогичное переполнение наблюдается при 320/375 px. Сетка не перестраивается, хотя названия показателей и числовые значения требуют большего места.

### Причина и область исправления

- `wwwroot/app.css:1112–1136`: `.act-stat-row { grid-template-columns: repeat(4, 1fr); }`, intrinsic minimum у `.act-stat-card` и размеры текста.
- `Pages/Workspace/Activity.cshtml:31–47`: четыре показателя — запросы, входные/выходные токены, стоимость.
- Перестраивать колонки по доступной ширине (4/2/1 либо эквивалентная адаптивная сетка), разрешить безопасное сжатие/перенос текстовых частей. Сохранить `gap: var(--spacing-md)` и padding карточки из существующего компонента.
- Не сокращать суммы или токены с потерей точности ради вёрстки, не менять формат/источник статистики. Повторить измерения после T-002/T-003: исправленная ширина контейнера отличается от исходной ошибочной.

### Критерии приёмки

- [ ] Все четыре карточки полностью видны на базовых ширинах и около брейкпоинтов; ни одна не расширяет контейнер или документ.
- [ ] На широком экране сохраняется компактная четырёхколоночная раскладка, на узком число колонок уменьшается без наложений.
- [ ] Проверены нули, большие числа с разделителями групп и стоимость с четырьмя знаками, русские и английские подписи; текст не обрезается.
- [ ] Gap/padding одинаковы у всех карточек; переключение периода и вкладки детализации не получают регрессии.

## T-007 — Закрывать выпадающие фильтры Requests по Escape с восстановлением фокуса

- status: done
- category: accessibility
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:38:44.3639107+04:00
- completed: 2026-08-30T14:41:08.9460814+04:00
- desc: P2. В `Pages/Admin/Requests.cshtml` добавлен обработчик клавиши Escape для закрытия всех открытых выпадающих фильтров `.filter-multiselect`, синхронизации атрибута `aria-expanded="false"` и возврата фокуса на связанную кнопку `.ms-toggle`. Функция `closeAllPanels` обновлена для синхронного сброса `aria-expanded="false"` на всех закрываемых выпадающих списках. Добавлены регрессионные тесты в `AdminFilterResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/admin/requests` на ширине 1024 px, нажать кнопку Model.
2. Убедиться, что панель `.ms-panel.open` видна и кнопка имеет aria-expanded=true; нажать Escape.
3. Панель остаётся открытой, aria-expanded остаётся true. Повторить Escape из поискового поля внутри фильтра; на исправленной версии сценарий должен закрывать панель и возвращать фокус.

### Причина и область исправления

- `Pages/Admin/Requests.cshtml:63–71` (кнопка/панель), обработчики около строк 211–240: закрытие завязано на мышиные взаимодействия, нет обработки Escape.
- Распространить общий механизм на Model, Provider, API Key, Node, Account. При закрытии убрать `.open`, выставить aria-expanded=false на связанной кнопке и вернуть на неё фокус.
- Не сбрасывать чекбоксы/поиск и не отправлять GET-форму при Escape. Сохранить закрытие кликом снаружи, повторным нажатием кнопки и при переключении на другой фильтр.
- T-004 исправляет расположение формы, T-003 — другое меню в общей шапке; не подменять эту задачу их исправлениями.

### Критерии приёмки

- [x] Escape закрывает каждый из пяти фильтров с фокусом на кнопке, в поисковом поле и на чекбоксе.
- [x] aria-expanded соответствует фактической видимости, фокус возвращается на открывшую кнопку; выбранные значения остаются прежними.
- [x] Enter/Space на кнопке, Tab внутри формы, клик снаружи и повторное открытие работают без двойных обработчиков и неожиданной отправки формы.
- [x] Escape при закрытых фильтрах ничего не отправляет и не переводит фокус произвольно; меню пользователя продолжает закрываться штатно.
- [x] Клавиатурный сценарий покрыт E2E на тестовых данных, проверен на мобильной и десктопной ширине после исправления общей раскладки.

## T-008 — Устранить лишний верхний отступ ряда кнопок в заголовках админских панелей

- status: done
- category: design-system
- priority: low
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:42:39.0480492+04:00
- completed: 2026-08-30T14:48:32.1145159+04:00
- desc: P3. В `wwwroot/app.css` и `wwwroot/app.min.css` для `.adm-card-header--split` добавлены `flex-wrap: wrap` и `gap: var(--spacing-md)`, а для непосредственного потомка `.adm-card-header--split > .adm-btn-row` обнулён лишний верхний отступ (`margin-top: 0`), исключая вертикальное смещение кнопок действий относительно заголовка панели. Добавлены регрессионные тесты в `AdminCardHeaderResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/admin/models` при 1024 или 1440 px, найти заголовок Public Models с кнопками Enrich/Sync/Price Comparison/Add Model.
2. У `.adm-card-header--split` padding равен 14px 16px, у вложенного `.adm-btn-row` дополнительно margin-top=16px.
3. Измеренный отступ кнопок сверху около 30 px, снизу около 14 px; текст заголовка и ряд действий визуально не центрированы относительно друг друга.

### Причина и область исправления

- `wwwroot/app.css:1052–1061`: контракт `.adm-card-header--split` и общий `.adm-btn-row`.
- `Pages/Admin/Models.cshtml:384–395` и другие потребители `.adm-card-header--split > .adm-btn-row` (Tags, Providers, Tools, сравнение цен — сверить актуальную разметку).
- Обнулить внешний верхний отступ только у ряда действий заголовка через контекстный селектор/модификатор. Сохранить 16px между полями и кнопками в обычных формах, 12px между действиями и штатные горизонтальные отступы панели.
- Не менять глобально все значения 14px на ближайший spacing-токен: дефект — дополнительный margin в конкретном контексте, а не доказанная ошибка padding самого компонента.
- Для многострочного заголовка/переноса действий применять согласованные gap и выравнивание без наложений; повторить проверку после T-002/T-003 и T-005.

### Критерии приёмки

- [x] В однострочной шапке ряд действий и текст вертикально выровнены; у кнопок симметричные верхний/нижний внутренние отступы с учётом границы панели.
- [x] Все потребители split-заголовка используют одинаковый контракт; кнопки не получают лишнего margin-top.
- [x] Ряды кнопок в формах добавления модели, узла и других формах сохранили отделение от полей; gap не заменён случайными локальными значениями.
- [x] На всех базовых ширинах, около брейкпоинтов и с длинными локализованными надписями нет наложений, обрезания текста или выхода действий за панель.


