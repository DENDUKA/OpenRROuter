# ContentService — CLAUDE.md

Каталог **инструментов** (Tools) — сторонних приложений (IDE, CLI, чат-клиенты, агенты),
которые можно подключить к нашему API. Админ ведёт их через WebApp (`/admin/tools`,
`/admin/tools/edit/{id?}`), публика читает на `/tools` и `/tools/{id}`.
**gRPC-сервер (локально 5007)** + HTTP-эндпоинты здоровья. Единственный потребитель — WebApp.
Layered: Domain → Application → Infrastructure. Только Dapper/Npgsql, EF нет.

## Bootstrap ([Program.cs](Program.cs) → [Infrastructure/Startup/Startup.cs](Infrastructure/Startup/Startup.cs))

- `AddSeqLogging("content")` + `AddOpenTelemetryMetrics` / `MapOpenTelemetryEndpoints`;
- `ApplySqlMigrationsAsync("ContentDb", "content", …)` **до** `ConfigurePipeline` (гасится
  `SqlMigrations:Enabled=false` — так делают gRPC-тесты);
- `AddGrpc` с серверным `ServiceAuthInterceptor` и `Max{Receive,Send}MessageSize = 8 MiB`
  (картинка до 1 MiB + framing; на клиенте в `WebApp/Program.cs` выставлено то же значение —
  **менять надо в двух местах**);
- `ContentLimitsOptions` биндится с `ValidateDataAnnotations().ValidateOnStart()` — вылет
  за `[Range]` роняет сервис на старте;
- pipeline: `UseLoggedExceptionHandler("/error")`, `MapGrpcService<GrpcContentService>()`,
  `MapControllers()`, `MapHealthChecks("/health")`;
- **`public partial class Program;` в конце Program.cs нужен** `WebApplicationFactory<Program>`
  в тестах — не удалять.

Особенности csproj: SDK — `Microsoft.NET.Sdk` (не `Sdk.Web`), поэтому `appsettings*.json`
подключены руками через `<Content Include>`, иначе не попадают в образ.
`content.proto` подключён с `GrpcServices="Both"`.

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities/` | `Tool.cs` (`Tool`, `ToolSummary`, `ToolLogo`, enum `LogoUpdate`), `ToolImage.cs` (`ToolImage`, `ToolImageContent`), `ToolCategory.cs` |
| Application | `Application/Services/` | `ContentApplicationService` (вся валидация и нормализация), `ContentErrorCodes`, `ImageContentTypeDetector` |
| Application | `Application/` | `Abstractions/Persistence/IContentRepository`, `Exceptions/ContentValidationException`, `Options/ContentLimitsOptions`, `Extensions/ContentExtensions` (DI: `AddContentInfrastructure` / `AddContentApplicationServices`) |
| Infrastructure | `Infrastructure/Grpc/` | `GrpcContentService` + `ContentGrpcMapper` |
| Infrastructure | `Infrastructure/Repository/` | `ContentRepository` (Dapper/PG) + `InMemoryContentRepository` |
| Infrastructure | `Infrastructure/Persistence/` | `ContentDbConnectionFactory` (`IContentDbConnectionFactory`), `ContentEntities`, `ContentPersistenceMapper` |
| Infrastructure | `Infrastructure/Auth/` | `ServiceAuthInterceptor` + `ServiceAuthOptions` |
| Infrastructure | `Infrastructure/Http/` | `HealthController` (`/healthz`, `/ready`, `/`) |

Все регистрации — singleton, время берётся из инжектированного `TimeProvider`.

## Публичный и админский контур — разделены по RPC, а не флагом

Черновик (`is_published = false`) невидим публике. Это обеспечено **разными RPC**, чтобы
s2s-интерцептор мог защитить их по имени метода:

| Публичные (без токена) | Админские (s2s) |
|---|---|
| `ListTools`, `GetTool`, `GetToolLogo`, `GetToolImage`, `ListToolCategories` | `ListToolsForAdmin`, `GetToolForAdmin`, `UpsertTool`, `DeleteTool`, `SetToolPublished`, `ListToolImages`, `UploadToolImage`, `DeleteToolImage`, `UpsertToolCategory`, `DeleteToolCategory` |

- `GetTool` на черновике отдаёт `found=false` — не 404-ошибку и не пустой `Tool`.
- `ListToolImages` защищён намеренно: он раскрывает имена файлов неопубликованных инструментов.
- **`GetToolLogo`/`GetToolImage` не смотрят на `is_published`** — байты черновика отдаются
  тому, кто знает id/uuid. Секретность черновика опирается на неугадываемость uuid картинки
  и на то, что публичные списки его не показывают; в фильтр по публикации это не завёрнуто.
- В публичных чтениях `ContentValidationException` (кривой слаг в query string) гасится в
  «пусто/не найдено», а не в ошибку.

## s2s-аутентификация

`ServiceAuthInterceptor` (`Infrastructure/Auth/`) — те же два слоя, что в BillingService:
caller-id в метадате `x-orr-caller`, общий секрет в `x-orr-s2s-token`, сравнение
constant-time (`CryptographicOperations.FixedTimeEquals`). Единственный ожидаемый caller —
`webappadmin`.

Инварианты, которые нельзя ослаблять:

- **Набор защищённых методов живёт в коде** (`ServiceAuthInterceptor.ProtectedMethods`),
  политики — в `appsettings.json` → `ServiceAuth:MethodPolicies`. Нет политики у защищённого
  метода → `Unauthenticated` (fail closed), а не «пропустить».
- Пустой/отсутствующий токен для caller-а в `ServiceAuth:Tokens` = caller не может
  аутентифицироваться (в `appsettings.json` `webappadmin` намеренно пустой, значение приходит
  из env/секрета).
- Рассинхрон «код ↔ конфиг» в обе стороны ловит `ServiceAuthPolicyCoverageTests`
  (читает **настоящий** `src/OpenRussiaRouter.ContentService/appsettings.json`):
  добавил админский RPC — добавь и политику.
- На стороне WebApp токен берётся из `ServiceAuth:ContentCaller`/`ServiceAuth:ContentToken`
  **с фолбэком на billing-овые** `ServiceAuth:BillingCaller`/`BillingToken` — в compose/k8s
  заданы только billing-овые, нового секрета контент не вводит.

## Логотип — три состояния апсерта, не два

`UpsertToolAsync(tool, LogoUpdate, logoBytes, logoContentType)`. Без enum `LogoUpdate`
репозиторий не отличит «админ не трогал логотип» от «админ удалил логотип» — оба приходят
как `null`-байты:

- `Keep` — колонки `logo_*` в SQL не переписываются (`case when @TouchLogo …`);
- `Replace` — новые байты, с проверкой типа;
- `Clear` — обе колонки в `null`.

В proto этого enum нет: `GrpcContentService.UpsertTool` выводит состояние из
`clear_logo` → `Clear`, иначе непустые `logo_bytes` → `Replace`, иначе `Keep`.
Отсюда следствие: **чтобы удалить логотип, обязателен флаг `clear_logo`** — пустые байты
означают «не трогать».

Проверка типа логотипа несимметрична картинкам: разрешены `png/jpeg/webp/svg+xml`,
и решает **заявленный** content-type, но для растровых форматов он обязан совпасть
с magic-байтами. У SVG magic-байтов нет, поэтому он принимается на слово — SVG допущен
только для логотипа.

## Картинки в markdown

Отдельная таблица `content.tool_images` (uuid PK, bytea, FK `ON DELETE CASCADE`).
Ключевые правила:

- **content-type определяется по magic-байтам** (`ImageContentTypeDetector`: png/jpeg/gif/webp),
  заявленный клиентом игнорируется полностью. `PublicToolImageController` в WebApp эхом
  возвращает сохранённый тип браузеру, поэтому доверять слову вызывающего нельзя — иначе
  `text/html` под видом `.png` поедет с нашего origin.
- **`image/svg+xml` для картинок в теле запрещён** (разрешён только для логотипа) — SVG скриптуем.
- Имя файла санитизируется (`Path.GetFileName` + вычистка запрещённых символов, ≤120 символов,
  фолбэк `image`) и на content-type никак не влияет.
- Лимиты дублируются DB-констрейнтами (`ck_tool_images_type`, `ck_tool_images_size`) —
  приложение не единственная линия обороны.
- Загрузка требует уже существующего инструмента (FK). Админка поэтому сохраняет черновик
  до того, как разблокировать панель картинок.

## Что НЕ тащим через gRPC

`ToolSelectSql` и `ToolSummarySelectSql` **не селектят `logo_bytes`** — отдают только
`(logo_bytes is not null) as HasLogo`. Каталог рендерится на каждый заход, и таскать bytea
всех логотипов в каждом ответе — пустая трата. Байты берёт только `GetToolLogoAsync`.

## Конфигурация

| Ключ | Смысл |
|---|---|
| `ConnectionStrings:ContentDb` | Postgres. **Пусто → `InMemoryContentRepository`** |
| `ContentLimits:MaxLogoBytes` | 256 KiB (зеркалит `ck_tools_logo`) |
| `ContentLimits:MaxImageBytes` | 1 MiB (зеркалит `ck_tool_images_size`) |
| `ContentLimits:MaxImagesPerTool` | 30 |
| `ContentLimits:MaxMarkdownChars` | 64 KiB — на каждое из `description_*`/`setup_*` |
| `ContentLimits:MaxTaglineChars` | 300 |
| `ContentLimits:MaxDisplayNameChars` | 200 (и на имена категорий) |
| `ContentLimits:MaxIdChars` | 50 — слаг `^[a-z0-9][a-z0-9._/-]*$`, зеркалит `ck_*_id` |
| `ServiceAuth:Tokens`, `ServiceAuth:MethodPolicies` | см. раздел про s2s |
| `SqlMigrations:Enabled` | `false` выключает применение миграций на старте |

Ловушки конфигурации:

- В `appsettings.json` `ContentDb` — **непустой обрывок** строки (`Pooling=…`, без `Host`).
  Фолбэк на in-memory включается не сам собой, а тем, что `appsettings.Development.json`
  перетирает ключ пустой строкой. В Production строка приходит из env/секрета целиком
  (`ConnectionStrings__ContentDb`); без env сервис не «тихо уйдёт в память», а упадёт на
  старте — `ContentDbConnectionFactory` строит `NpgsqlDataSource` из обрывка без `Host`.
- Нормализация тегов **захардкожена**, не конфиг: lowercase, ≤50 символов на тег,
  уникальные, не больше 20.

## БД

Схема `content` — [db/migrations/content/001_initial.sql](../../db/migrations/content/001_initial.sql);
применённые файлы не редактируем, только добавляем следующий префикс.
[002_api_base_url_placeholder.sql](../../db/migrations/content/002_api_base_url_placeholder.sql) —
разовая data-миграция: вшитые `https://api.{openrussiarouter,rawrter}.com` в `description_*`/`setup_*`
заменены на плейсхолдер `{{API_BASE_URL}}`.

- **Хост API в markdown инструментов не вшивать — только `{{API_BASE_URL}}`** (без `/v1` и хвостового
  слэша: `{{API_BASE_URL}}/v1`). Подставляет его WebApp при рендере (`ToolDetailsModel`), сервис
  хранит текст как есть. Иначе гайды снова отстанут от смены домена API, как после переезда на rawrter.

- `content.tool_categories` — слаг PK, `name_ru`/`name_en`, `sort_order`;
  миграция засевает `ide`, `cli`, `chat-ui`, `agent`, `other`.
- `content.tools` — слаг PK, `category_id → tool_categories(id) ON DELETE SET NULL`,
  `tags text[]`, парные `tagline_*`/`description_*`/`setup_*`, `logo_bytes`+`logo_content_type`,
  `is_published`, `sort_order`. Констрейнты: `ck_tools_id`, `ck_tools_logo` (тип + 256 KiB),
  `ck_tools_website` (только `http(s)://`).
- `content.tool_images` — uuid PK, `tool_id → tools(id) ON DELETE CASCADE`, `bytes`, `byte_size`
  (с проверкой `octet_length(bytes) = byte_size`).
- **Сортировка везде `sort_order DESC`**, потом `display_name`/`name_en` — больший
  `sort_order` наверху. `InMemoryContentRepository` обязан повторять этот порядок.

## Частые заблуждения (НЕ повторять)

| Неверно | Верно |
|---|---|
| Невалидный ввод → `RpcException` | Мутирующие RPC возвращают `upserted/deleted/updated=false` + `error_code` (`ContentErrorCodes`). Исключения наружу не летят |
| `Tool.HasLogo` вычисляется из `LogoBytes` | В `Tool` байтов нет вовсе — `HasLogo` приходит из SQL |
| Удаление категории удаляет её инструменты | `ON DELETE SET NULL` — инструменты остаются без категории |
| Достаточно проверить `IFormFile.ContentType` | Он клиентский. Для картинок в теле — только magic-байты |
| `deleted=true` значит, что строка была | `DeleteTool`/`DeleteToolCategory` не проверяют наличие строки и всегда отдают `deleted=true` (`error_code=category_not_found` не используется никем) |
| Новый метод в `.proto` защищён по умолчанию | Защита — по явному списку `ProtectedMethods`; забыл добавить — метод публичный |

## Порты и деплой

- Локально: `launchSettings` поднимает 5007 c `Kestrel__EndpointDefaults__Protocols=Http2`.
- Compose (`5007:8080`): весь 8080 переведён в Http2, HTTP-эндпоинты здоровья там
  фактически недоступны.
- k8s (`infra/kustomize/base/services/content-service.yaml`): два слушателя — HTTP 8080
  (пробы `/health`) и gRPC 8081 (Http2), а `Service content-service:8080` смотрит
  на **targetPort 8081**. Поэтому `GrpcServices__Content=http://content-service:8080`
  корректен, а новый HTTP-эндпоинт через этот Service недостижим.
- Порт 5006 не занимать — это HTTP коллектора в compose.

## Тесты (`tests/OpenRussiaRouter.ContentService.Tests`)

| Файл | Что проверяет |
|---|---|
| `Application/ContentApplicationServiceTests.cs` | валидация: слаги, лимиты, url, категории, три состояния логотипа, нормализация тегов, `created_at` при апсерте |
| `Application/GrpcContentServiceIntegrationTests.cs` | `WebApplicationFactory<Program>` с in-memory репозиторием: публичный/админский раздел, работа интерцептора, конвенция `error_code` |
| `Application/ServiceAuthPolicyCoverageTests.cs` | `ProtectedMethods` ↔ `ServiceAuth:MethodPolicies` в обе стороны |
| `Infrastructure/Persistence/ContentRepositoryPostgresTests.cs` (+ `PostgresTestDatabase.cs`) | Testcontainers postgres:16-alpine, без Docker тихо скипается; проверяет каскады, `SET NULL`, DB-констрейнты. Override строки — `ORR_CONTENT_POSTGRES_TEST_CONNECTION` |
