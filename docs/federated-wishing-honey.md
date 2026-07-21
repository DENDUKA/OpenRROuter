# Каталог инструментов (Tools) — новый микросервис ContentService

## Context

Пользователям нужно показать, в каких сторонних приложениях (IDE, CLI, чат-клиенты, агенты)
можно использовать наш OpenAI-совместимый API, и дать пошаговую инструкцию подключения.
Сейчас такого раздела нет — есть только `/models`, `/providers` и общий `/docs`.

Что делаем:
1. Админка — CRUD инструментов (пункт в боковом меню `_AdminLayout`).
2. Описание и инструкция — **Markdown**, с загрузкой картинок (скриншотов) прямо в описание.
3. Публичный каталог `/tools` (по образцу `/providers`) + детальная `/tools/{id}` с полным
   описанием и разделом «Как подключить».

**Решение по хранению (выбрано пользователем): новый микросервис `ContentService`** со своей
БД, а не расширение BillingService. Контент — не деньги, поэтому отдельный сервис;
цена решения — полный инфраструктурный обвес (этап 4).

Прочие принятые решения: картинки — в БД отдельной таблицей (bytea), локализация ru/en
полями (как `DescriptionRu`/`DescriptionEn` у провайдеров), карточка содержит логотип,
краткое описание и категорию/теги.

---

## Этап 1. Бэкенд: ContentService

### 1.1 Миграция `db/migrations/content/001_initial.sql`

```sql
CREATE SCHEMA IF NOT EXISTS content;

CREATE TABLE IF NOT EXISTS content.tool_categories (
    id             text PRIMARY KEY,              -- slug: 'ide', 'cli', 'chat-ui', 'agent'
    name_ru        text NOT NULL,
    name_en        text NOT NULL,
    sort_order     int  NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tool_categories_id CHECK (id ~ '^[a-z0-9][a-z0-9._/-]{0,49}$')
);

CREATE TABLE IF NOT EXISTS content.tools (
    id                text PRIMARY KEY,           -- человекочитаемый slug → /tools/cursor
    display_name      text NOT NULL,
    category_id       text REFERENCES content.tool_categories(id) ON DELETE SET NULL,
    tags              text[] NOT NULL DEFAULT '{}',
    website_url       text,
    tagline_ru        text,   tagline_en     text,   -- короткое описание для карточки
    description_ru    text,   description_en text,   -- markdown «что это»
    setup_ru          text,   setup_en       text,   -- markdown «как подключить»
    logo_bytes        bytea,  logo_content_type text,
    is_published      boolean NOT NULL DEFAULT false,
    sort_order        int     NOT NULL DEFAULT 0,
    created_at_utc    timestamptz NOT NULL DEFAULT now(),
    updated_at_utc    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tools_id CHECK (id ~ '^[a-z0-9][a-z0-9._/-]{0,49}$'),
    CONSTRAINT ck_tools_logo CHECK (logo_bytes IS NULL OR (
        logo_content_type IN ('image/png','image/jpeg','image/webp','image/svg+xml')
        AND octet_length(logo_bytes) <= 262144)),
    CONSTRAINT ck_tools_website CHECK (website_url IS NULL OR website_url ~* '^https?://')
);
CREATE INDEX IF NOT EXISTS idx_tools_published_sort ON content.tools (is_published, sort_order DESC, display_name);
CREATE INDEX IF NOT EXISTS idx_tools_category       ON content.tools (category_id);

CREATE TABLE IF NOT EXISTS content.tool_images (
    id             uuid PRIMARY KEY,              -- Guid.NewGuid() на стороне приложения
    tool_id        text NOT NULL REFERENCES content.tools(id) ON DELETE CASCADE,
    file_name      text NOT NULL,
    content_type   text NOT NULL,
    byte_size      int  NOT NULL,
    bytes          bytea NOT NULL,
    alt_text       text,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tool_images_type CHECK (content_type IN ('image/png','image/jpeg','image/webp','image/gif')),
    CONSTRAINT ck_tool_images_size CHECK (byte_size > 0 AND byte_size <= 1048576
        AND octet_length(bytes) = byte_size)
);
CREATE INDEX IF NOT EXISTS idx_tool_images_tool ON content.tool_images (tool_id, created_at_utc DESC);

INSERT INTO content.tool_categories (id, name_ru, name_en, sort_order) VALUES
    ('ide',     'IDE и редакторы',     'IDEs & editors',      100),
    ('cli',     'CLI и терминал',      'CLI & terminal',       90),
    ('chat-ui', 'Чат-интерфейсы',      'Chat interfaces',      80),
    ('agent',   'Агенты и фреймворки', 'Agents & frameworks',  70),
    ('other',   'Прочее',              'Other',                 0)
ON CONFLICT (id) DO NOTHING;
```

Решения: `svg+xml` разрешён только для логотипа (рендерится в `<img>`), но не для картинок
в теле. Лимит 30 картинок на инструмент — в application-слое. Удаление категории —
`SET NULL` (инструмент выживает, попадает в бакет «Без категории»).

### 1.2 `src/OpenRussiaRouter.Contracts/Protos/content.proto`

`package openrussiarouter.content.v1`, `csharp_namespace = "OpenRussiaRouter.Contracts.Grpc.Content.V1"`.

Публичные RPC (без s2s): `ListTools`, `GetTool`, `GetToolLogo`, `GetToolImage`, `ListToolCategories`.
Админские (под s2s): `ListToolsForAdmin`, `GetToolForAdmin`, `UpsertTool`, `DeleteTool`,
`SetToolPublished`, `ListToolImages`, `UploadToolImage`, `DeleteToolImage`,
`UpsertToolCategory`, `DeleteToolCategory`.

Черновики защищены разделением RPC, а не флагом в запросе: публичный `GetTool` на
неопубликованном инструменте отдаёт `found=false`.

Messages: `ToolSummary` (карточка — **без** markdown-тел и без байтов логотипа, только
`has_logo`), `Tool`, `ToolCategory`, `ToolImage` (метаданные без байтов) + пары
Request/Response. Конвенция ответов репозитория соблюдается: `bool upserted/deleted/found`
+ `string error_code`, без RpcException наружу (образец — `billing.proto:660-710`).

`error_code`: `invalid_id`, `invalid_display_name`, `invalid_url`, `text_too_long`,
`unknown_category`, `logo_too_large`, `logo_type_not_allowed`, `image_too_large`,
`image_type_not_allowed`, `image_limit_reached`, `tool_not_found`, `image_not_found`.

Лимиты сообщений: `AddGrpc(o => o.MaxReceiveMessageSize = 8 * 1024 * 1024)` на сервере
и `MaxSend/ReceiveMessageSize` на канале WebApp — картинки до 1 MiB.

### 1.3 Проект `src/OpenRussiaRouter.ContentService/`

Полностью по образцу `src/OpenRussiaRouter.ControlPlane/`:

- `.csproj` — `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App` (**не** `Sdk.Web`),
  `<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\content.proto" GrpcServices="Both">`,
  явный блок `<Content Include="appsettings*.json">`, версии пакетов — только в `Directory.Packages.props`.
- `Program.cs` — `AddSeqLogging("content")`, `AddOpenTelemetryMetrics(...)`,
  `await app.ApplySqlMigrationsAsync("ContentDb", "content", RepositoryMigrationPath.ForService("content"))`,
  `MapOpenTelemetryEndpoints()`, в конце `public partial class Program;` (нужен для `WebApplicationFactory`).
- `Domain/Entities/` — `Tool.cs`, `ToolImage.cs`, `ToolCategory.cs` (sealed record).
- `Application/Abstractions/Persistence/IContentRepository.cs`,
  `Application/Options/ContentLimitsOptions.cs` (MaxLogoBytes 256 KiB, MaxImageBytes 1 MiB,
  MaxImagesPerTool 30, MaxMarkdownChars 64 KiB, MaxTaglineChars 300),
  `Application/Services/ContentApplicationService.cs` (вся валидация),
  `ContentErrorCodes.cs`, `Exceptions/ContentValidationException.cs` (несёт `ErrorCode`),
  `Extensions/ContentExtensions.cs` — `AddContentInfrastructure(connectionString)` с фолбэком
  на `InMemoryContentRepository` при пустой строке + `AddContentApplicationServices`.
- `Infrastructure/Persistence/` — `ContentDbConnectionFactory`, `*Entity`, `ContentPersistenceMapper`.
- `Infrastructure/Repository/ContentRepository.cs` (Dapper, `on conflict (id) do update`)
  **и** `InMemoryContentRepository.cs` — обе реализации обязательны (правило репо).
- `Infrastructure/Grpc/GrpcContentService.cs` + `ContentGrpcMapper.cs`.
- `Infrastructure/Auth/` — `ServiceAuthOptions.cs` + `ServiceAuthInterceptor.cs`, копия пары из
  `src/OpenRussiaRouter.BillingService/Infrastructure/Auth/ServiceAuthInterceptor.cs`.
- `Infrastructure/Startup/Startup.cs`, `Infrastructure/Http/HealthController.cs`, `Dockerfile`, `CLAUDE.md`.

**Важно по производительности:** `ListToolSummariesAsync` и `GetToolAsync` **не должны**
селектить `logo_bytes` — отдельные `ToolSummarySelectSql`/`ToolSelectSql` без bytea-колонки.
Байты логотипа берёт только `GetToolLogoAsync`.

### 1.4 s2s-авторизация

ControlPlane её не имеет, но здесь нужна: единственный писатель — WebApp, и запись
админская. Копируем паттерн BillingService — `ProtectedMethods` в интерсепторе +
`ServiceAuth:MethodPolicies` в `appsettings.json` со значением `["webappadmin"]` для всех
десяти админских RPC. Токен — тот же `ServiceAuth__Tokens__webappadmin`, новых секретов нет.

`ListToolImages` тоже защищаем — он раскрывает имена файлов черновиков.

**Порт: 5007.** ⚠️ Изначально в плане стоял 5006 — **занят**: в `deploy/compose/compose.yml`
и `compose.prod.yml` коллектор маппит `5006:8080` (его HTTP-эндпоинт, по нему же ходит
health-чек в `update-orr.sh`). Выяснилось при реализации, сервис переведён на 5007.
В контейнере как у всех — 8080 HTTP/1.1 (health + метрики) и 8081 HTTP/2 (gRPC).

### 1.5 Регистрация в solution

`OpenRussiaRouter.sln`: сервис + тест-проект — по 4 секции на каждый (Project/EndProject,
ProjectConfigurationPlatforms ×4 строки, NestedProjects в папки `src`/`tests`).

---

## Этап 2. WebApp — админка

### 2.1 gRPC-клиент

`src/OpenRussiaRouter.WebApp/Program.cs` — блок после ControlPlane: `GrpcServices:Content`
(дефолт `http://localhost:5006`), `AddGrpcClient` + `ConfigureChannel` (тот же
`SocketsHttpHandler` keep-alive, плюс лимиты сообщений 8 MiB) +
`GrpcDeadlineInterceptor` + `ServiceAuthClientInterceptor`, затем `AddScoped<ContentProvider>()`.

`Services/Content/` — `ContentProvider.cs`, `ContentContracts.cs` (records `ToolSummaryInfo`,
`ToolInfo`, `ToolLogo`, `ToolImageInfo`, `ToolImageContent`, `ToolCategoryInfo`),
`ContentException.cs` — точный аналог `Services/Billing/BillingProvider.cs`
(`if (!response.Upserted) throw new ContentException(response.ErrorCode)`).

### 2.2 Страницы админки — две, не одна

Схема «форма над таблицей» из `Pages/Admin/Tags.cshtml` здесь не годится: четыре
markdown-textarea + логотип + библиотека картинок. Берём схему отдельного редактора
(`Pages/Admin/NodeEdit.cshtml`):

- **`Pages/Admin/Tools.cshtml(.cs)`** — только список: логотип-миниатюра, id, название,
  категория, badge «Published/Draft», sort order. Действия: Edit (ссылка), Publish/Unpublish
  (`OnPostTogglePublishAsync`), Delete (`OnPostDeleteAsync`). `[Authorize(Roles = "Admin")]`,
  `PageModelBase`, PRG через `TempData["SuccessMessage"]`.
- **`Pages/Admin/ToolEdit.cshtml(.cs)`** — `@page "/admin/tools/edit/{id?}"`. Секции:
  идентичность (Id readonly при редактировании, DisplayName, категория select, теги,
  WebsiteUrl, SortOrder, IsPublished), логотип (`logoFile` + `clearLogo` — как в
  `Pages/Admin/Providers.cshtml.cs:89-130`), вкладки ru/en с Tagline + Description + Setup,
  панель библиотеки картинок.
  Хендлеры: `OnGetAsync(string? id)`, `OnPostSaveAsync()` (редирект на себя же —
  `RedirectToPage(new { id = Form.Id })`), `OnPostUploadImageAsync()` (AJAX → `JsonResult`),
  `OnPostDeleteImageAsync(Guid imageId)`.

**Загрузка картинки требует существующего `tool_id`** (FK). Поэтому у нового инструмента
панель картинок заблокирована с подсказкой «Сохраните инструмент, чтобы загружать картинки»;
первый Save создаёт черновик (`is_published=false` по умолчанию), после чего панель
разблокируется. Отсюда и редирект редактора на себя, а не на список.

### 2.3 UX вставки картинки в markdown

`OnPostUploadImageAsync` принимает `Request.Form.Files.GetFile("imageFile")` + `toolId`,
валидирует, вызывает `ContentProvider.UploadToolImageAsync` и возвращает:

```json
{ "ok": true, "id": "<uuid>", "url": "/tools/image/<uuid>", "markdown": "![alt](/tools/image/<uuid>)" }
```

Клиент — `wwwroot/js/tool-editor.js` (vanilla JS, без фреймворков — как в остальном
приложении), подключается только из `ToolEdit.cshtml` через `@section Scripts`:
- скрытый `<input type="file">`, drop-зона и **listener на `paste`** для каждой markdown-textarea
  (вставка скриншота из буфера — основной сценарий);
- `fetch` с заголовком `RequestVerificationToken` из `@Html.AntiForgeryToken()`
  (именованные хендлеры Razor Pages требуют токен);
- при успехе — вставка markdown по позиции каретки (`selectionStart`) в сфокусированную
  textarea + миниатюра в ленту библиотеки;
- лента показывает все картинки инструмента с кнопками «скопировать markdown» и «удалить».

### 2.4 Пункт меню

`Pages/Shared/_AdminLayout.cshtml` — новый `<a class="@SidenavClass("/admin/tools")"
href="/admin/tools">` после `/admin/providers`, с инлайновой feather-подобной SVG-иконкой.
`SidenavClass` матчит по префиксу — пункт останется подсвеченным на `/admin/tools/edit/x`.

---

## Этап 3. Публичные страницы

### 3.1 Безопасность markdown

`MarkdownRenderer.Render` ([Services/Markup/MarkdownRenderer.cs](src/OpenRussiaRouter.WebApp/Services/Markup/MarkdownRenderer.cs))
относительные `<img src>` пропускает, так что `![](/tools/image/{uuid})` отрендерится
без правок. Но добавляем **новую перегрузку**, не трогая существующие вызовы (`ModelDetails`):

```csharp
public static string RenderStrict(string? markdown, string allowedImagePrefix = "/tools/image/")
```

- пайплайн с `.DisableHtml()` — Markdig экранирует сырой HTML на парсинге, а не полагается
  на regex-скраббер (тот не является настоящим HTML-санитайзером);
- после рендера — вырезание `<img>`, чей `src` не начинается с `allowedImagePrefix`:
  внешняя картинка это трекинг-пиксель, утекающий IP/UA каждого посетителя третьей стороне,
  плюс mixed-content и риск недоступности.

К `ModelDetails` это **не** применять — описания из OpenRouter могут легитимно ссылаться
на внешние изображения.

### 3.2 Контроллеры отдачи бинарей

- `Controllers/PublicToolLogoController.cs` — `[Route("tools/logo")]`, `GET {id}`,
  `Cache-Control: public, max-age=604800`, фолбэк — SVG-монограмма.
  Генератор монограммы вынести из `Controllers/PublicProviderLogoController.cs` в
  `Services/Markup/MonogramSvg.cs` и вызывать из обоих контроллеров (не копипастить).
- `Controllers/PublicToolImageController.cs` — `[Route("tools/image")]`,
  `[HttpGet("{id:guid}")]` (route-constraint отсекает мусор до сервиса),
  `Cache-Control: public, max-age=31536000, immutable` (контент неизменяем в рамках uuid),
  `Content-Disposition: inline`, явный `X-Content-Type-Options: nosniff`.

### 3.3 Страницы

- `Pages/Tools.cshtml(.cs)` — `@page`, `Layout = "~/Pages/Shared/_CatalogLayout.cshtml"`,
  по образцу [Pages/Providers.cshtml](src/OpenRussiaRouter.WebApp/Pages/Providers.cshtml):
  hero + чипы фильтра по категориям (серверные, `?category=ide`) + грид карточек
  (логотип, название, tagline по текущей культуре с фолбэком, чипы тегов).
  Инлайновый `<style>` в конце файла — как у Providers.
- `Pages/ToolDetails.cshtml(.cs)` — `@page "/tools/{id}"`, `Layout = "~/Pages/Shared/_Layout.cshtml"`.
  Шапка (логотип, название, категория, теги, ссылка на сайт), затем два markdown-блока:
  описание и заметно выделенный раздел «Как подключить». Фолбэк культуры на каждое поле —
  как в `ProviderDetails`: `ru ? (Ru ?? En) : (En ?? Ru)`.

### 3.4 Навигация и ресурсы

- `Pages/Shared/_Layout.cshtml:68-77` — `<a class="@TopNavClass("/tools")" href="/tools">@Localizer["Nav_Tools"]</a>`
  между Providers и Docs.
- `Resources/SharedResource.{ru,en}.resx` — `Nav_Tools` и ключи ошибок страниц.
- Новые `Resources/Pages/Tools.{ru,en}.resx`, `Resources/Pages/ToolDetails.{ru,en}.resx`.
- Админские страницы оставляем с англоязычными строками в разметке — как `Admin/Providers`
  (админский resx в репо не заведён, не заводим).
- `wwwroot/app.css` — классы админского редактора (`.md-editor`, `.md-image-strip`);
  стили каталожных страниц — инлайново, по существующему прецеденту.

---

## Этап 4. Инфраструктура и деплой — НЕ ВЫПОЛНЕН (по решению пользователя)

> Этапы 1–3 реализованы и покрыты тестами. Этап 4 **отменён**: пользователь разворачивает
> сам («не билди в docker, достаточно написать код, чтобы это всё собиралось»).
> Правки, которые я успел внести в `build-deploy.yml`, `deploy-prod.yml`, `update-orr.sh`
> и `build-push.ps1`, **откачены** — без парных блоков в compose они ломали существующий
> деплой (`update-orr.sh` без аргументов пытался бы собрать сервис, отсутствующий в compose).
> Ниже — чек-лист того, что осталось сделать вручную.

- **Compose** (3 файла: `docker-compose.yml`, `deploy/compose/compose.yml`,
  `deploy/compose/compose.prod.yml`): сервис `content-service`, host-порт **5006**,
  `Kestrel__Endpoints__*` (8080 Http1 / 8081 Http2; в prod — метрики на 8082, Prometheus
  не умеет h2c), `ConnectionStrings__ContentDb`, `ServiceAuth__Tokens__webappadmin`;
  в блок webapp — `GrpcServices__Content`.
- **Prometheus**: `prometheus.yml` (`content-service:8080`), `deploy/compose/prometheus.prod.yml` (`:8082`).
- **CI**: `.github/workflows/build-deploy.yml` — запись в матрице
  (`service: content-service`, `output_key: content_service`, dockerfile) + **4 точки** в
  bash-скрипте path-детекта (объявление переменной, `case` по пути, ветка `all_services`,
  финальный `case` по `output_key`). `deploy-prod.yml` — input + env `PICK_CONTENT` +
  **добавить в список `envs:`** (иначе не долетит по SSH) + `TO_DEPLOY`.
  `deploy/compose/update-orr.sh` — `ALL_SERVICES`, валидирующий `case`, health, финальный grep.
  `build-push.ps1` — `$ALL_SERVICES` + `$SERVICE_PATTERNS`.
- **k8s**: `infra/kustomize/base/services/content-service.yaml` (Deployment + Service,
  `port 8080 → targetPort 8081`) + запись в `base/services/kustomization.yaml` +
  `images:` в `overlays/dev/services/kustomization.yaml`.
  `base/infra/postgres-secret.yaml` — `content-connection` (БД `orr_content`).
  `base/infra/migration-job.yaml` — `orr_content` в цикл создания БД.
  **`base/infra/network-policies.yaml`** — в namespace default-deny, забыть = gRPC timeout
  errno 110: новая ingress-политика для `content-service` (from webapp, порты 8080/8081),
  добавить `app: content-service` в `allow-ingress-to-postgres` и в
  `allow-prometheus-scrape-apps`.
  `infra/argocd/orr-dev-app.yaml` — `image-list` + `update-strategy: digest`.
- **GHCR**: новый пакет `content-service` создаётся только под `secrets.GHCR_PAT`
  (write:packages), не под `GITHUB_TOKEN` — в workflow PAT уже используется, проверить,
  что секрет существует.
- **Документация**: корневой `CLAUDE.md` — строка в таблице проектов/портов и в карте
  страниц WebApp.

---

## Тесты

`tests/OpenRussiaRouter.ContentService.Tests/` (csproj — копия `OpenRussiaRouter.ControlPlane.Tests`):

- **`ContentApplicationServiceTests`** (на `InMemoryContentRepository`): регекс слага
  (отклонить `Cursor`, `../x`, 51 символ), лимиты длин, тип/размер логотипа и картинки,
  31-я картинка → `image_limit_reached`, неизвестная категория, сохранение `CreatedAtUtc`
  при апдейте, `clear_logo=true` обнуляет обе колонки, апсерт без нового логотипа
  сохраняет старый.
- **`ContentRepositoryTests`** (Testcontainers PG, при отсутствии Docker скипаются):
  round-trip включая `text[]`, `publishedOnly` прячет черновики и уважает `sort_order DESC`,
  фильтр по категории, каскадное удаление картинок вместе с инструментом,
  `GetToolAsync` не возвращает байты логотипа, DB-constraint'ы отклоняют мусор.
- **`GrpcContentServiceTests`** (`WebApplicationFactory<Program>` + `GrpcChannel`):
  публичный `ListTools` скрывает черновики; `GetTool` на черновике → `found=false`,
  `GetToolForAdmin` → отдаёт; невалидный upsert → `upserted=false` + правильный `error_code`,
  не RpcException; `UploadToolImage` → `GetToolImage` round-trip байтов.
- **`ServiceAuthInterceptorTests`**: каждый защищённый метод отклоняет отсутствующий/чужой
  токен; публичные проходят без метаданных; **отдельный тест сверяет набор
  `ProtectedMethods` с ключами `MethodPolicies` из поставляемого `appsettings.json`** —
  ловит классическую ошибку «добавил RPC, забыл политику».

WebApp-тесты: расширить `MarkdownRendererTests` (`RenderStrict` экранирует сырой HTML,
относительный `/tools/image/{guid}` выживает, внешний `https://…png` вырезается,
поведение старого `Render` не изменилось); page-model тесты `/tools` и `/tools/{id}`
с мокнутым `ContentProvider` (фолбэк культуры, фильтр, not-found, исключение провайдера →
`ErrorMessage`, а не 500); `/admin/tools` требует роль Admin.

---

## Проверка (end-to-end)

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build   OpenRussiaRouter.sln --no-restore -nr:false -m:1   # warning ломает build
dotnet test    tests/OpenRussiaRouter.ContentService.Tests --no-build -nr:false -m:1
dotnet test    tests/OpenRussiaRouter.WebApp.Tests          --no-build -nr:false -m:1
```

Ручной прогон после этапа 3 (`dotnet run --project src/OpenRussiaRouter.ContentService`
и `... .WebApp`):
1. `/admin/tools` → «Add tool» → заполнить id/название/категорию → Save (создаётся черновик).
2. В редакторе вставить скриншот из буфера в поле Setup → картинка загружается, markdown
   вставляется по каретке, миниатюра появляется в ленте.
3. `/tools` — черновика **нет**. Publish → появился, фильтр по категории работает.
4. `/tools/{id}` — оба markdown-блока рендерятся, картинка отдаётся с `/tools/image/{uuid}`.
5. Переключить язык — тексты меняются, при пустом en показывается ru (и наоборот).
6. Негатив: загрузить .exe под видом png (magic-байты не совпадут) и картинку >1 MiB —
   обе отклонены; вставить в markdown `<script>` и внешний `<img src="https://…">` —
   после `RenderStrict` в HTML их нет.

---

## Критерии готовности этапов

| Этап | Готово, когда |
|---|---|
| 1 | Тесты ContentService зелёные; сервис поднимается на 5006; `ListTools` отдаёт `[]`; upsert с чужим токеном отклонён |
| 2 | Админ создаёт черновик, вставляет скриншот из буфера, сохраняет, перезагружает — всё на месте |
| 3 | `/tools` показывает только опубликованные; `/tools/{id}` рендерит оба блока с картинками; ru/en переключается |
| 4 | dev-кластер зелёный, `/tools` доступна через задеплоенный WebApp, `content-service` скрейпится Prometheus |
