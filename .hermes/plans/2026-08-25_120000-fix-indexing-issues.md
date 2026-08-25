# План устранения проблем индексации rawrter.com в Google Search Console

> **Для Hermes:** Используй навык `subagent-driven-development` для пошаговой реализации и аудита задач из этого плана.

**Цель:** Устранить ошибки индексации в Google Search Console для домена `rawrter.com`, оптимизировать sitemap/robots.txt, очистить битые 404-ссылки и сократить долю исключённых страниц в индексе.

**Архитектура решения:** Анализ причин в GSC и их сопоставление с кодовой базой `OpenRussiaRouter.WebApp` (ASP.NET Core 8 / Razor Pages):
1. Аудит и валидация динамического генератора `SitemapEndpoint.cs` и `robots.txt`.
2. Анализ и исправление 404-ошибок (обработка удаленных/отключенных моделей, редиректы).
3. Проверка цепочек редиректов (`LocalePrefixMiddleware`, `ModelDetailRoute`).
4. Разграничение намеренно закрытых страниц (`noindex`) и полезного каталога.
5. Усиление внутренней перелинковки для ускорения сканирования страниц со статусом «Обнаружена, не проиндексирована».

**Стек технологий:** .NET 8 (C#), ASP.NET Core Razor Pages, xUnit, Caddy, VitePress, Google Search Console API / Web Interface.

---

### Анализ текущей ситуации (по данным Google Search Console и кодовой базы)

| Причина в Search Console | Кол-во URL | Причина в кодовой базе / инфраструктуре | Корректность / Действие |
|---|---|---|---|
| **Не найдено (404)** | 14 | Ссылки на удалённые/переименованные модели или старые пути `openrussiarouter.com` | **Требует исправления**: аудит 404 URL, добавление 301 редиректов или удаление ссылок |
| **Индексирование запрещено тегом `noindex`** | 10 | Намеренная директива `<meta name="robots" content="noindex,follow">` в `_Layout.cshtml` для `/login`, `/signup`, `/workspace/*`, `/admin/*` и `StatusCode >= 400` | **Ожидаемое поведение** для приватных зон. Проверить, не попадают ли страницы каталога |
| **Страница с переадресацией (301)** | 9 | `http://` → `https://`, голые пути `/` → `/ru`, неканонические слаги моделей `ModelDetailRoute` (напр. `/models/DeepSeek/deepseek-v4-flash` → `/models/deepseek/deepseek-v4-flash`) | **Ожидаемо при миграции**, но нужно исключить попадание старых URL в `sitemap.xml` и внутренние ссылки |
| **Вариант страницы с тегом `canonical`** | 5 | Параллельные версии страниц (`ru`/`en`) или альтернативные URL со ссылкой на канонический | **Корректно**. Убедиться в зеркальности `hreflang` и `canonical` |
| **Обнаружена, не проиндексирована** | 11 | Новые модели/инструменты добавлены в `sitemap.xml`, но у Googlebot недостаточно краулингового бюджета / веса внутренних ссылок | **Требует улучшения**: перелинковка из категорий, тегов и главной страницы |
| **Заблокировано в `robots.txt`** | - | Пути `/admin/`, `/workspace/`, `/webhook/`, `/internal/`, `/health`, `/metrics` | **Корректно** |

---

## Задачи по реализации и оптимизации

### Task 1: Аудит и выгрузка списка проблемных URL из Google Search Console

**Цель:** Получить точный список всех 14 URL со статусом 404, 10 URL со статусом `noindex` и 9 URL со статусом Redirect из отчёта Search Console.

**Действия:**
1. Через интерфейс GSC или экспорт (`ЭКСПОРТИРОВАТЬ` в Google Таблицы / CSV) выгрузить детализированные списки URL для категорий:
   - *«Не найдено (404)»*
   - *«Индексирование запрещено тегом noindex»*
   - *«Страница с переадресацией»*
2. Сохранить список в файл `docs/seo/gsc-audit-2026-08.md`.

---

### Task 2: Проверка и исправление генератора карты сайта `SitemapEndpoint.cs`

**Цель:** Гарантировать, что `GET /sitemap.xml` отдаёт **только** канонические 200 OK URL без 301-редиректов, 404-ошибок и страниц с `noindex`.

**Файлы:**
- Изменить: `src/OpenRussiaRouter.WebApp/Api/SitemapEndpoint.cs`
- Тесты: `tests/OpenRussiaRouter.WebApp.Tests/SitemapEndpointTests.cs`

**Шаг 1: Написать тест на отсутствие невалидных и неканонических URL в sitemap**
```csharp
[Fact]
public async Task Sitemap_ContainsOnlyCanonicalUrls_WithNoRedirectsOrDisabledModels()
{
    // Arrange & Act via WebApplicationFactory
    using var response = await client.GetAsync("/sitemap.xml");
    var xml = await response.Content.ReadAsStringAsync();
    
    // Assert: проверка, что все провайдеры в URL приведены к нижнему регистру / ProviderId
    Assert.DoesNotContain("/models/DeepSeek/", xml); // пример проверки кейсинга
    Assert.Contains("/models/deepseek/", xml);
}
```

**Шаг 2: Проверить логику `SitemapEndpoint.Collect`**
- Убедиться, что `ModelDetailRoute.TryBuildPath` строит строго канонические пути.
- Убедиться, что отключенные модели (`IsActive == false`) и пустые провайдеры отфильтровываются.

**Шаг 3: Запустить тесты**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~SitemapEndpointTests`

---

### Task 3: Настройка обработки удалённых и переименованных моделей (404 / 410 / 301)

**Цель:** Устранить 14 ошибок 404, отдавая 301 Redirect на актуальную модель (если есть аналог) или 410 Gone / понятную страницу ошибки без поломки краулера.

**Файлы:**
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/ModelDetails.cshtml.cs`
- Изменить: `src/OpenRussiaRouter.WebApp/Services/Catalog/ModelDetailRoute.cs`
- Тесты: `tests/OpenRussiaRouter.WebApp.Tests/ModelDetailsPageTests.cs`

**Шаг 1: Добавить таблицу алиасов / редиректов для устаревших идентификаторов моделей**
В `ModelDetailRoute.cs` поддержать сопоставление переименованных моделей:
```csharp
private static readonly Dictionary<string, string> ModelAliases = new(StringComparer.OrdinalIgnoreCase)
{
    // Старые слаги -> новые канонические слаги
    ["deepseek-coder-v2"] = "deepseek-coder-v2.5",
    // ... алиасы из аудита 404
};
```

**Шаг 2: Обработка в `ModelDetailsModel.OnGetAsync`**
Если модель не найдена по прямому совпадению, проверить алиас и отдать `RedirectPermanent`.

**Шаг 3: Запустить тесты**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~ModelDetailsPageTests`

---

### Task 4: Проверка закрытия от индексации служебных URL (`noindex` & `robots.txt`)

**Цель:** Убедиться, что Googlebot не тратит краулинговый бюджет на обход страниц авторизации и кабинета.

**Файлы:**
- Проверить: `src/OpenRussiaRouter.WebApp/wwwroot/robots.txt`
- Проверить: `src/OpenRussiaRouter.WebApp/Pages/Shared/_Layout.cshtml`
- Тесты: `tests/OpenRussiaRouter.WebApp.Tests/RobotsTxtTests.cs`
- Тесты: `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs`

**Шаг 1: Проверить `robots.txt`**
Добавить в `robots.txt` запрет на обход страниц входа/регистрации, чтобы бот не ходил по ним лишний раз:
```
Disallow: /login
Disallow: /signup
Disallow: /forgot-password
Disallow: /reset-password
```

**Шаг 2: Проверить тесты**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~RobotsTxtTests`

---

### Task 5: Оптимизация внутренней перелинковки для «Обнаружена, не проиндексирована»

**Цель:** Повысить авторитетность и ускорить индексацию 11 страниц моделей/инструментов, которые Google обнаружил, но пока не включил в индекс.

**Файлы:**
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/Models.cshtml` (список популярных моделей / теги)
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml` (витрина моделей на главной)
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/ModelDetails.cshtml` (блок похожих моделей `RelatedModels`)

**Действия:**
1. Проверить, что на всех карточках моделей на главной (`/ru` и `/en`) и в каталоге (`/ru/models`) используются прямые ссылки с хелпером `asp-page="/ModelDetails"` и каноническими параметрами `provider` и `slug`.
2. Убедиться, что блок `RelatedModels` в `ModelDetails.cshtml` выводит до 8 ссылок на соседние модели того же провайдера, предотвращая появление страниц-тупиков (orphan pages).

---

### Task 6: Запуск валидации исправления в Google Search Console

**Цель:** Уведомить Googlebot о внесенных исправлениях для повторного обхода.

**Действия:**
1. После деплоя на прод выполнить `ПРОВЕРИТЬ ИСПРАВЛЕНИЕ` (Validate Fix) в Search Console для отчетов:
   - *«Страница с переадресацией»*
   - *«Не найдено (404)»*
2. Отправить повторно `https://rawrter.com/sitemap.xml` в разделе *«Файлы Sitemap»*.
3. Для ключевых страниц моделей воспользоваться инструментом *«Проверка URL»* → *«Запросить индексирование»*.

---

## Верификация и метрики успеха

1. **Код и тесты:** Все тесты решения проходят успешно (`dotnet test`).
2. **Sitemap:** Валидатор `https://rawrter.com/sitemap.xml` возвращает 100% URL со статусом HTTP 200 (0 редиректов, 0 404, 0 noindex).
3. **GSC динамика:** В течение 7–14 дней после валидации количество ошибок 404 снижается до 0, а статус «Обнаружена, не проиндексирована» переходит в «Проиндексировано».
