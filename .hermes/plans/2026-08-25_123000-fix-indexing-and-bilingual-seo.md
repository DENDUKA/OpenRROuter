# План устранения проблем индексации rawrter.com в Google Search Console с учётом двуязычной структуры (/ru и /en)

> **Для Hermes:** Используй навык `subagent-driven-development` для пошаговой реализации и аудита задач из этого плана.

**Цель:** Устранить реальные ошибки индексации в Google Search Console (54 неиндексированные страницы), нормализовать обход краулером новой двуязычной структуры (`/ru/...` и `/en/...`), оптимизировать краулинговый бюджет и ускорить индексацию новых страниц каталога.

**Архитектура решения:**
Сайт перешёл на явную адресацию локалей: русский контент живёт под префиксом `/ru/...` (по умолчанию), английский — под `/en/...` (через `LocalePrefixMiddleware` и `LocaleUrl`). Голые пути (`/`, `/models`, `/tools`) отдают 301-редирект на `/ru/...`.
1. **404 (14 страниц):** Анализ запрашиваемых Googlebot путей, добавление 301-алиасов для переименованных/перенесённых моделей в `ModelDetailRoute.cs` и `ModelDetailsModel.cs`.
2. **`noindex` (10 страниц):** Оптимизация `robots.txt` (запрет обхода `/login`, `/signup`, `/workspace/*` и др.), чтобы Googlebot не тратил краулинговый бюджет на сканирование страниц, которые заведомо закрыты тегом `noindex` в `_Layout.cshtml`.
3. **Редиректы 301 (9 страниц):** Обеспечить, чтобы `sitemap.xml`, внутренняя перелинковка и разметка (`LocalePathTagHelper`) ссылались **напрямую на канонические локализованные адреса** (`/ru/...` и `/en/...`), исключая ссылки на голые редиректящие пути и старые кейсинги провайдеров.
4. **Обнаружена, не проиндексирована (11 страниц) и Crawled — not indexed:** Усиление внутренней перелинковки между русской и английской версиями, улучшение доступности страниц моделей через витрину, категории и блок `RelatedModels`.
5. **Валидация в Google Search Console:** Отправка запросов на валидацию исправлений и обновление sitemap.

**Стек технологий:** .NET 8 (C#), ASP.NET Core Razor Pages, xUnit, Caddy, Google Search Console.

---

### Анализ статусов Google Search Console в контексте локализации `/ru` и `/en`

| Статус в Search Console | Кол-во | Причина в кодовой базе и архитектуре | Необходимое действие |
|---|---|---|---|
| **Не найдено (404)** | 14 | Старые URL без префикса, удалённые/переименованные модели или ссылки с опечатками | Аудит 404 URL, маппинг алиасов в `ModelDetailRoute` с 301-редиректом на актуальные модели |
| **Индексирование запрещено тегом `noindex`** | 10 | Служебные страницы (`/login`, `/signup`, `/verify-email`, `/workspace/*`, пустые провайдеры) | Добавить директивы `Disallow` в `robots.txt`, чтобы краулер не тратил запросы на их обход |
| **Страница с переадресацией (301)** | 9 | Голые пути (`/`, `/models` → `/ru/...`) и неканонический регистр (напр. `/models/DeepSeek/...`) | Проверить, что ни одна внутренняя ссылка и ни один `sitemap.xml` не содержат голых или неканонических путей |
| **Вариант страницы с тегом `canonical`** | 5 | Связка альтернативных версий страниц (`/ru` и `/en`) через `rel="canonical"` и `hreflang` | Убедиться в 100% зеркальности `LocaleUrl.Alternates` и `x-default` (указывает на `/en/...`) |
| **Обнаружена, не проиндексирована** | 11 | Удвоение числа страниц после внедрения `/en` и `/ru`; робот ещё не успел обойти весь пул | Усилить внутреннюю перелинковку (хлебные крошки, витрина, похожие модели) для ускорения обхода |

---

## Задачи по реализации

### Task 1: Аудит 404 URL и формирование таблицы перенаправлений устаревших моделей

**Цель:** Собрать точный список 14 URL с ошибкой 404 из Search Console и подготовить маппинг на канонические локализованные адреса.

**Файлы:**
- Создать: `docs/seo/gsc-404-redirects-map.md`
- Изменить: `src/OpenRussiaRouter.WebApp/Services/Catalog/ModelDetailRoute.cs`
- Тест: `tests/OpenRussiaRouter.WebApp.Tests/ModelDetailRouteTests.cs`

**Шаг 1: Написать failing test на сопоставление устаревших/альтернативных слагов моделей**
```csharp
[Theory]
[InlineData("DeepSeek/deepseek-v4-flash", "deepseek", "deepseek-v4-flash")]
[InlineData("deepseek-coder-v2", "deepseek", "deepseek-coder-v2.5")]
public void TryResolveLegacyModelPath_ReturnsCanonicalPath(string rawInput, string expectedProvider, string expectedSlug)
{
    var path = ModelDetailRoute.TryResolveAliasPath(rawInput);
    path.Should().Be($"/models/{expectedProvider}/{expectedSlug}");
}
```

**Шаг 2: Запустить тест и убедиться в FAIL**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~TryResolveLegacyModelPath`

**Шаг 3: Реализовать логику разрешения алиасов в `ModelDetailRoute.cs`**
Добавить словарь известных переименований / устаревших путей моделей и метод `TryResolveAliasPath(...)`.

**Шаг 4: Запустить тест и убедиться в PASS**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~TryResolveLegacyModelPath`

---

### Task 2: Обработка 404 и редиректов в `ModelDetails.cshtml.cs` с учётом `/ru` и `/en`

**Цель:** При запросе устаревшего URL модели выполнять 301 Permanent Redirect на канонический путь с сохранением текущей локали пользователя.

**Файлы:**
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/ModelDetails.cshtml.cs`
- Тест: `tests/OpenRussiaRouter.WebApp.Tests/ModelDetailsPageTests.cs`

**Шаг 1: Написать failing test для редиректа устаревшего слага с сохранением `PathBase`**
```csharp
[Fact]
public async Task Get_LegacyModelSlug_RedirectsPermanentlyToCanonicalWithLocalePrefix()
{
    // Arrange
    using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Act
    using var response = await client.GetAsync("/ru/models/DeepSeek/deepseek-v4-flash");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
    response.Headers.Location.Should().Be(new Uri("/ru/models/deepseek/deepseek-v4-flash", UriKind.Relative));
}
```

**Шаг 2: Запустить тест и проверить результат**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~Get_LegacyModelSlug`

**Шаг 3: Реализовать логику редиректа в `OnGetAsync`**
В `ModelDetailsModel.OnGetAsync` внедрить проверку алиасов перед вызовом `NotFound()`:
```csharp
if (ModelData is null && ModelDetailRoute.TryResolveAliasPath(Slug) is { } aliasPath)
{
    return RedirectPermanent(Request.PathBase + aliasPath + Request.QueryString);
}
```

**Шаг 4: Запустить тесты и убедиться в PASS**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~ModelDetailsPageTests`

---

### Task 3: Оптимизация `robots.txt` для сохранения краулингового бюджета

**Цель:** Исключить повторное сканирование страниц со статусом `noindex` (авторизация, кабинеты, служебные API), добавив их в `robots.txt`.

**Файлы:**
- Изменить: `src/OpenRussiaRouter.WebApp/wwwroot/robots.txt`
- Тест: `tests/OpenRussiaRouter.WebApp.Tests/RobotsTxtTests.cs`

**Шаг 1: Написать failing test для проверки расширенного списка Disallow в `robots.txt`**
```csharp
[Theory]
[InlineData("Disallow: /login")]
[InlineData("Disallow: /signup")]
[InlineData("Disallow: /forgot-password")]
[InlineData("Disallow: /reset-password")]
[InlineData("Disallow: /verify-email")]
[InlineData("Disallow: /preferences")]
public async Task RobotsTxt_ContainsAuthAndUtilityDisallowRules(string expectedRule)
{
    using var client = factory.CreateClient();
    var content = await client.GetStringAsync("/robots.txt");
    content.Should().Contain(expectedRule);
}
```

**Шаг 2: Обновить `src/OpenRussiaRouter.WebApp/wwwroot/robots.txt`**
```
User-agent: *
Allow: /

# Приватные и служебные разделы
Disallow: /admin/
Disallow: /workspace/
Disallow: /webhook/
Disallow: /internal/
Disallow: /health
Disallow: /metrics

# Страницы авторизации и управления профилем (закрыты тегом noindex)
Disallow: /login
Disallow: /signup
Disallow: /forgot-password
Disallow: /reset-password
Disallow: /verify-email
Disallow: /preferences
Disallow: /invite/
Disallow: /pay/

# Карты сайта
Sitemap: https://rawrter.com/sitemap.xml
Sitemap: https://rawrter.com/docs/sitemap.xml
```

**Шаг 3: Запустить тест и убедиться в PASS**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~RobotsTxtTests`

---

### Task 4: Аудит и верификация генерации `sitemap.xml` для `/ru` и `/en`

**Цель:** Проверить, что динамический `GET /sitemap.xml` строит исключительно 200 OK канонические URL с корректными `xhtml:link rel="alternate"` и без голых путей без префикса.

**Файлы:**
- Проверить: `src/OpenRussiaRouter.WebApp/Api/SitemapEndpoint.cs`
- Тест: `tests/OpenRussiaRouter.WebApp.Tests/SitemapEndpointTests.cs`

**Шаг 1: Написать тест на валидацию каждого URL в `sitemap.xml`**
```csharp
[Fact]
public async Task Sitemap_AllUrls_ArePrefixedWithLocale_AndReturnOk()
{
    using var client = factory.CreateClient();
    using var response = await client.GetAsync("/sitemap.xml");
    var xml = await response.Content.ReadAsStringAsync();
    var doc = XDocument.Parse(xml);
    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    
    var locs = doc.Root!.Elements(ns + "url").Select(u => u.Element(ns + "loc")!.Value).ToList();
    
    foreach (var loc in locs)
    {
        var uri = new Uri(loc);
        if (!uri.AbsolutePath.StartsWith("/docs"))
        {
            uri.AbsolutePath.Should().MatchRegex("^/(ru|en)(/|$)", $"URL {loc} must start with /ru or /en");
        }
    }
}
```

**Шаг 2: Запустить тесты sitemap**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests --filter FullyQualifiedName~SitemapEndpointTests`

---

### Task 5: Улучшение внутренней перелинковки для «Обнаружена, не проиндексирована»

**Цель:** Обеспечить глубину ссылок на новые модели и инструменты каталога, передавая краулинговый вес с главной `/ru` и `/en` страниц.

**Файлы:**
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml` (блок популярных моделей с прямыми ссылками на канонические `/ru/models/...` и `/en/models/...`)
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/Models.cshtml` (фильтры по провайдерам со ссылками на `/providers/{id}`)
- Изменить: `src/OpenRussiaRouter.WebApp/Pages/ModelDetails.cshtml` (блок похожих моделей `RelatedModels`)
- Тест: `tests/OpenRussiaRouter.WebApp.Tests/CatalogLinkingTests.cs`

**Шаг 1: Написать тест на отсутствие битых внутренних ссылок на страницах каталога**
```csharp
[Theory]
[InlineData("/ru/models")]
[InlineData("/en/models")]
[InlineData("/ru")]
[InlineData("/en")]
public async Task CatalogPages_ContainCanonicalModelLinks_WithoutBareRedirects(string pageUrl)
{
    using var client = factory.CreateClient();
    var html = await client.GetStringAsync(pageUrl);
    
    // Ссылки не должны вести на голый /models/... без префикса локали
    html.Should().NotContain("href=\"/models/");
}
```

**Шаг 2: Убедиться, что `LocalePathTagHelper` корректно подставляет `PathBase` во все ссылки `href="/models/..."`**

**Шаг 3: Запустить тесты**
`dotnet test tests/OpenRussiaRouter.WebApp.Tests`

---

### Task 6: Валидация и повторная отправка в Google Search Console

**Цель:** Инициировать повторный обход Googlebot и зафиксировать устранение проблем.

**Шаги в интерфейсе Search Console:**
1. В разделе **«Индексирование страниц»** открыть категорию **«Не найдено (404)»** → нажать **«ПРОВЕРИТЬ ИСПРАВЛЕНИЕ»**.
2. Открыть категорию **«Страница с переадресацией»** → нажать **«ПРОВЕРИТЬ ИСПРАВЛЕНИЕ»**.
3. В разделе **«Файлы Sitemap»** отправить заново `https://rawrter.com/sitemap.xml`.
4. В инструменте **«Проверка URL»** протестировать 3-4 ключевые страницы моделей (например, `https://rawrter.com/ru/models/deepseek/deepseek-v4-flash` и `https://rawrter.com/en/models/deepseek/deepseek-v4-flash`) и запросить индексирование.

---

## Чек-лист готовности к деплою
- [ ] Все unit- и интеграционные тесты проходят: `dotnet test` (438+ тестов).
- [ ] В `robots.txt` добавлены `Disallow` для страниц авторизации/утилит.
- [ ] `SitemapEndpoint` генерирует 100% валидные URL для `/ru` и `/en`.
- [ ] Настроены 301-алиасы для известных старых 404 URL моделей.
- [ ] Запущен процесс валидации в Search Console.
