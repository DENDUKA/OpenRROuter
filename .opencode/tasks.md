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

---

> Источник: SEO-план первичного запуска, 2026-08-06.
> 22 задачи: SEO-001..SEO-022. Приоритеты: high — обязательны до первой индексации;
> medium — повышение в выдаче; low — метрика. Операционные (SEO-002, SEO-019..SEO-022) — blocked.
> Рекомендуемый порядок: SEO-004 → SEO-005 → SEO-006 → SEO-007 (high-пакет),
> затем SEO-001/003 → SEO-008..SEO-015 → SEO-016/017 → SEO-018; low-блок — отдельно.

## SEO-001 — Карта ключевых запросов (keyword map)
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: Составить keyword map минимум для 10 стартовых кластеров: «ИИ API», «AI API», «API нейросетей», «OpenAI-совместимый API», «OpenRouter оплатить», «OpenRouter API пополнить», «Модели и цены», «API конкретной модели», «Провайдеры моделей». Для каждой посадочной страницы зафиксировать primary/secondary keyword и intent; не назначать один точный запрос одновременно главной и /ai-api; определить title, H1, description и CTA. Артефакт — markdown-файл docs/seo/keyword-map.md с таблицей keyword → URL → intent → статус → показы → клики → регистрация. Документ, не код; сборка solution не меняется.

## SEO-002 — Подключить системы вебмастеров и аналитику
- status: blocked
- blocked_reason: код внедрения готов (теги, CSP, события конверсии), DNS подтверждён; осталось задеплоить и вручную настроить цели в GA4/Метрике и проверить конверсии
- priority: low
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed:
- desc: Зарегистрировать домен в Google Search Console и Яндекс Вебмастере через DNS-подтверждение; настроить Яндекс Метрику и (при необходимости) Google Analytics 4; связать Search Console и GA4; настроить цели: регистрация, подтверждение email, создание API-ключа, первое пополнение. Критерий: домен подтверждён в обеих системах, события конверсии приходят в аналитику. Код готов: AnalyticsOptions + теги в _Layout.cshtml (GA4 G-BDD3DYVCLL, Метрика 111420552, CSP-хосты в Program.cs), события signup/email_verified/api_key_created/topup_completed, прод-значения в compose.prod.yml, тесты в SeoHeadTests/LegalConsentTests. Runbook — docs/seo/webmasters-analytics-runbook.md.

## SEO-003 — Список index/noindex URL
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: Зафиксировать стратегию: поисковик всегда получает русскую версию публичного сайта как основную; hreflang для cookie-локализации не строить (один URL не представляет две языковые версии); подготовить архитектуру для будущих /ru/... и /en/..., но англоязычный SEO-контур не публиковать. Индексируемые URL: /, /models, /models/{provider}/{slug}, /providers, /providers/{id}, /tools, /tools/{id} (только опубликованные с содержательным описанием), /docs, /support, юридические страницы. Неиндексируемые: /login, /signup, /forgot-password, /reset-password, /verify-email, /invite/*, /workspace/*, /admin/*, /pay/*, /error, служебные API и webhooks. Артефакт — список index/noindex, согласованный с продуктом.

## SEO-004 — Исправить soft 404 в каталогах
- status: todo
- priority: high
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Обработчик /tools/{id} (Pages/Tools.cshtml.cs): отсутствующий или неопубликованный tool возвращает NotFound() вместо HTML с сообщением при статусе 200. Обработчик /providers/{id} (Pages/Providers.cshtml.cs): отсутствующий provider возвращает NotFound(). Пользовательскую страницу 404 сохранить. Добавить unit/integration тесты: существующая сущность → 200; несуществующая → 404; черновик tool недоступен публично (404). Сборка и тесты WebApp обязательны.

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

## SEO-009 — Переработать главную страницу
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Pages/Index.cshtml: title «ИИ API и OpenAI-совместимый API для моделей | OpenRussiaRouter», meta description «Единый API для AI-моделей: OpenAI-совместимый формат, каталог моделей, прозрачные цены и пополнение баланса в России.» H1 сохранить, ниже добавить блоки: что такое ИИ API; для кого подходит; какие модели доступны; как получить API-ключ; как оплачивать и пополнять баланс; чем отличается от прямой интеграции с поставщиками. FAQ на 5-7 вопросов. Ссылки на /models, /docs, /ai-api, страницу оплаты и ключевые provider/model pages. Блок «Популярные модели» с HTML-ссылками. Рекламное утверждение «на 25% дешевле OpenRouter» — оставлять только при доказуемой методике сравнения (уточнить у продукта). Критерий: title/H1/первый экран/FAQ покрывают целевой кластер без переспама.

## SEO-010 — Создать посадочную страницу «ИИ API» (/ai-api)
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Новая страница Pages/AiApi.cshtml + AiApi.cshtml.cs с директивой @page "/ai-api", H1 «ИИ API для подключения AI-моделей в одном интерфейсе». Блоки о том, что получает разработчик: единый OpenAI-совместимый endpoint; API-ключ; модели разных провайдеров; цены и расходы; лимиты по ключам; каталог моделей. Примеры на Python, Node.js и cURL. Таблица сценариев: чат; код; обработка документов; vision; генерация речи (если функция доступна — уточнить у продукта). FAQ: «Что такое ИИ API?», «Как получить API key?», «Какие модели доступны?», «Как пополнить баланс?», «Совместим ли API с OpenAI SDK?». Перелинковка на каталог моделей, документацию, signup и страницу пополнения. Локализация — два .resx (ru/en) по пути страницы; ссылка на страницу в соответствующем layout. Критерий: страница отвечает на коммерческий запрос до регистрации и содержит самостоятельную ценность. Сборка обязательна.

## SEO-011 — Создать страницу оплаты AI API (/openrouter-payment)
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Новая страница Pages/OpenRouterPayment.cshtml + OpenRouterPayment.cshtml.cs с директивой @page "/openrouter-payment", H1 «Как пополнить баланс для AI API в России». Не создавать впечатление, что OpenRussiaRouter — это OpenRouter или его официальный партнёр; не использовать логотипы, товарные знаки и формулировки «официальная оплата OpenRouter» без юридического основания (юридическую позицию уточнить у продукта). Честно описать: пользователю нужен доступ к моделям и пополнение баланса; OpenRussiaRouter предоставляет собственный OpenAI-совместимый API и собственный баланс. Сравнение только по проверяемым критериям: API-совместимость; модели; валюта и способ оплаты; формат биллинга; доступность для пользователей из России. CTA: зарегистрироваться, посмотреть модели, пополнить баланс. FAQ: «Можно ли оплатить OpenRouter через OpenRussiaRouter?», «Чем OpenRussiaRouter отличается от OpenRouter?», «Как пополнить баланс для AI API?», «Можно ли использовать OpenAI SDK?». Локализация — два .resx (ru/en); ссылка в layout. Критерий: страница перехватывает спрос по платёжному намерению без введения в заблуждение. Сборка обязательна.

## SEO-012 — Title и description каталогов
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. /models: title «Каталог AI-моделей и цены API | OpenRussiaRouter», description «Сравните AI-модели, стоимость входных и выходных токенов, контекст и возможности. Используйте модели через OpenAI-совместимый API.» /providers: title «Провайдеры AI-моделей в одном API | OpenRussiaRouter». /tools: title «Инструменты и интеграции для AI API | OpenRussiaRouter». Provider pages: «Модели {Provider}: цены, контекст и API | OpenRussiaRouter». Model pages: «{Model}: цена API, контекст и возможности | OpenRussiaRouter». Descriptions уникальные, не шаблон с заменой одного названия. Критерий: нет дублей title и meta description среди индексируемых публичных страниц. Сборка обязательна.

## SEO-013 — Quality gate для страниц моделей
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Минимальный набор данных для индексации модели: русское название и описание; назначение; входные и выходные modality; контекст; цены; пример вызова; поддерживаемые возможности; дата обновления технических данных. Добавить блок «Подходит для» с реальными сценариями и блок «Похожие модели» по provider, modality и ценовому сегменту (источник данных уточнить у продукта). Не публиковать в sitemap модели без русского описания или с пустым значимым контентом. При изменении цены или статуса модели обновлять lastmod в sitemap. Критерий: каждая модельная страница имеет уникальный текст и не является тонкой карточкой характеристик. Сборка обязательна.

## SEO-014 — Внутренняя перелинковка
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Название модели в таблице — обычная ссылка <a>, не строка с onclick. На model detail: ссылка на provider; ссылки на похожие модели; ссылка на каталог; ссылка на документацию; CTA к получению ключа. На provider detail: текстовый вводный блок о provider; ссылки на самые востребованные модели; ссылка на общий каталог. На /docs: ссылки на /ai-api, /models, конкретные примеры моделей, регистрацию. На главной — блок «Популярные модели» с обычными HTML-ссылками. Критерий: у всех приоритетных страниц минимум 2-3 входящие внутренние ссылки с понятным анкором. Сборка обязательна.

## SEO-015 — FAQ и структурированные данные (JSON-LD)
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Видимый FAQ на главной, /ai-api и /openrouter-payment. JSON-LD: Organization на главную; WebSite на главную; BreadcrumbList на detail-страницы (модели/провайдеры/тулы). FAQPage добавлять только если вопросы и ответы видимы пользователю; скрытый FAQ не размечать. Schema не заменяет нормальный контент. Проверить через Google Rich Results Test. Критерий: schema валидна в Rich Results Test и совпадает с видимым HTML. Сборка обязательна.

## SEO-016 — Решение по единому контуру документации
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: Принять решение по размещению документации: развивать Razor /docs; или разместить VitePress на docs.openrussiarouter.com; или развернуть VitePress под /docs/. Не держать две одинаковые документации на разных URL без чёткой canonical/redirect стратегии. Результат — зафиксированное решение (вариант размещения уточнить у продукта). Один документ — один индексируемый URL и понятная навигация к продукту. Это решение, не код.

## SEO-017 — SEO-настройка VitePress-документации
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: docs-site (VitePress, docs.openrussiarouter.com). В .vitepress/config.mts: абсолютный site; title и description; русская навигация; генерация sitemap; canonical на страницах; ссылки на главный сайт и signup. Добавить docs-site/public/robots.txt (Allow: / + ссылка на sitemap). Per-page frontmatter (title, description) во все 18 markdown-файлов (index/guide/api/legal × ru/en). Внимание: для docs-site нет CLAUDE.md — структура в desc; проверка сборки — npm run docs:build (не dotnet build). Документация не должна дублировать Razor /docs без canonical/redirect стратегии.

## SEO-018 — E-E-A-T и доверие
- status: todo
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: WebApp. Добавить страницу «О сервисе» или расширенный блок на главной: кто предоставляет сервис; какие API-возможности есть; как работает биллинг; как связаться с поддержкой. Убедиться, что публичны и доступны: политика конфиденциальности; оферта; контакты поддержки; условия оплаты и возвратов, если применимо. Добавить дату обновления на технических и документационных страницах. Создать редакционный процесс обновления моделей, цен и документации. Критерий: пользователь и поисковик могут проверить владельца сервиса, условия использования и актуальность технической информации. Сборка обязательна.

## SEO-019 — Проверка технического SEO перед индексацией
- status: blocked
- blocked_reason: нужен задеплоенный прод и адрес продакшен-домена
- priority: high
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: После деплоя технических SEO-изменений проверить: HTTP-коды ключевых URL; sitemap; robots; canonical и noindex. Критерий: ключевые URL отдают корректные коды, sitemap/robots/canonical/noindex валидны.

## SEO-020 — Добавить sitemap в поисковые системы
- status: blocked
- blocked_reason: нужен доступ к аккаунтам Google Search Console и Яндекс Вебмастер
- priority: high
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: Добавить sitemap в Google Search Console и Яндекс Вебмастер. Критерий: системы вебмастеров приняли sitemap без ошибок.

## SEO-021 — Ручной переобход приоритетных страниц
- status: blocked
- blocked_reason: нужен доступ к аккаунтам вебмастеров
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: Отправить на переобход: главную, /ai-api, /openrouter-payment, /models, 5-10 лучших модельных страниц (список уточнить у продукта).

## SEO-022 — Еженедельный мониторинг после запуска (90 дней)
- status: blocked
- blocked_reason: нужен доступ к системам вебмастеров/аналитики; непрерывный процесс, не разовая задача
- priority: low
- created: 2026-08-06T00:00:00Z
- started:
- completed:
- desc: Первые 12 недель еженедельно: Coverage/Pages и ошибки индексации; проверка попадания служебных URL в индекс; soft 404, duplicate canonical, crawled-not-indexed. Отслеживать показы по кластерам: «ИИ API», «AI API», «OpenRouter оплатить», «OpenRouter API пополнить». Улучшать title и description страниц с показами, но низким CTR. Добавлять контент только по запросам с показами или подтверждённым спросом. Раз в месяц обновлять цены, статусы моделей и sitemap. Анализировать конверсию organic → signup → API key → top-up. KPI: число индексируемых/проиндексированных URL; показы и клики по кластерам; CTR; регистрации из organic; конверсия organic → API key; organic → первое пополнение. Ориентиры на 90 дней: 100% технически корректных индексируемых URL в sitemap; 0 soft 404 в каталогах; 0 indexable URL без title/description/canonical; первые показы по «ИИ API», «AI API», «OpenAI-совместимый API»; первые позиции по long-tail запросам моделей; брендовый кластер OpenRouter оценивать по регистрациям и пополнениям отдельно.
