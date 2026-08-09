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
