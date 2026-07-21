# ContentService — CLAUDE.md

Каталог **инструментов** (Tools) — сторонних приложений (IDE, CLI, чат-клиенты, агенты),
которые можно подключить к нашему API. Админ ведёт их через WebApp, публика читает
на `/tools` и `/tools/{id}`. **gRPC-сервер (порт 5007)** + HTTP `/health`.
Порт 5006 занят HTTP-эндпоинтом коллектора в compose — не переиспользовать.
Layered: Domain → Application → Infrastructure. Только Dapper/Npgsql, EF нет.

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities` | `Tool`, `ToolSummary`, `ToolLogo`, `LogoUpdate`, `ToolImage`, `ToolImageContent`, `ToolCategory` |
| Application | `Application/Services` | `ContentApplicationService` (вся валидация), `ContentErrorCodes`, `ImageContentTypeDetector` |
| Application | `Application/Abstractions/Persistence` | `IContentRepository` |
| Infrastructure | `Infrastructure/Grpc` | `GrpcContentService` + `ContentGrpcMapper` |
| Infrastructure | `Infrastructure/Repository` | `ContentRepository` (PG) + `InMemoryContentRepository` |
| Infrastructure | `Infrastructure/Auth` | `ServiceAuthInterceptor` + `ServiceAuthOptions` |

## Публичный и админский контур — разделены по RPC, а не флагом

Черновик (`is_published = false`) невидим публике. Это обеспечено **разными RPC**, чтобы
s2s-интерцептор мог защитить их по имени метода:

| Публичные (без токена) | Админские (s2s) |
|---|---|
| `ListTools`, `GetTool`, `GetToolLogo`, `GetToolImage`, `ListToolCategories` | `ListToolsForAdmin`, `GetToolForAdmin`, `UpsertTool`, `DeleteTool`, `SetToolPublished`, `ListToolImages`, `UploadToolImage`, `DeleteToolImage`, `UpsertToolCategory`, `DeleteToolCategory` |

`GetTool` на черновике отдаёт `found=false` — не 404-ошибку и не пустой `Tool`.
`ListToolImages` защищён намеренно: он раскрывает имена файлов неопубликованных инструментов.

**Набор защищённых методов живёт в коде** (`ServiceAuthInterceptor.ProtectedMethods`), политики —
в `appsettings.json` → `ServiceAuth:MethodPolicies`. Отсутствие политики для защищённого метода
= отказ (fail closed). Рассинхрон этих двух списков ловит `ServiceAuthPolicyCoverageTests`.

## Логотип — три состояния апсерта, не два

`UpsertToolAsync(tool, LogoUpdate, logoBytes, logoContentType)`. Без enum `LogoUpdate`
репозиторий не отличит «админ не трогал логотип» от «админ удалил логотип» — оба приходят
как `null`-байты:

- `Keep` — колонки `logo_*` в SQL не переписываются (`case when @TouchLogo ...`);
- `Replace` — новые байты, с проверкой magic-байтов;
- `Clear` — обе колонки в `null`.

## Что НЕ тащим через gRPC

`ToolSelectSql` и `ToolSummarySelectSql` **не селектят `logo_bytes`** — отдают только
`(logo_bytes is not null) as HasLogo`. Каталог рендерится на каждый заход, и таскать bytea
всех логотипов в каждом ответе — пустая трата. Байты берёт только `GetToolLogoAsync`.

## Картинки в markdown

Отдельная таблица `content.tool_images` (uuid PK, bytea, FK `ON DELETE CASCADE`).
Ключевые правила:

- **content-type определяется по magic-байтам** (`ImageContentTypeDetector`), заявленный
  клиентом игнорируется. Эндпоинт отдачи эхом возвращает сохранённый тип браузеру, поэтому
  доверять слову вызывающего нельзя — иначе `text/html` под видом `.png` поедет с нашего origin.
- **`image/svg+xml` для картинок в теле запрещён** (разрешён только для логотипа) — SVG скриптуем.
- Лимиты: 1 MiB на картинку, 30 картинок на инструмент, 256 KiB на логотип.
  Дублируются DB-констрейнтами — приложение не единственная линия обороны.
- Загрузка требует уже существующего инструмента (FK). Админка поэтому сохраняет черновик
  до того, как разблокировать панель картинок.

## Частые заблуждения (НЕ повторять)

| Неверно | Верно |
|---|---|
| Невалидный ввод → `RpcException` | Мутирующие RPC возвращают `upserted/deleted=false` + `error_code`. Исключения наружу не летят |
| `Tool.HasLogo` вычисляется из `LogoBytes` | В `Tool` байтов нет вовсе — `HasLogo` приходит из SQL |
| Удаление категории удаляет её инструменты | `ON DELETE SET NULL` — инструменты остаются без категории |
| Достаточно проверить `IFormFile.ContentType` | Он клиентский. Проверяем байты |

## БД и тесты

Миграции `db/migrations/content/`, `ConnectionStrings:ContentDb`. Пустая строка подключения →
`InMemoryContentRepository` (локальный запуск и юнит-тесты).
PG-тесты — Testcontainers, override — `ORR_CONTENT_POSTGRES_TEST_CONNECTION`.
