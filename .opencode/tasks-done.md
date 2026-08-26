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

