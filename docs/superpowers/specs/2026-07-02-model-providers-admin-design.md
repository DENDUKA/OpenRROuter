# Провайдеры моделей в админке

## Summary

Добавить админский справочник провайдеров моделей: `/admin/providers` со списком, добавлением и редактированием. Провайдер хранит `id`, display name, логотип, описание на русском и английском. На `/admin/models?action=edit` поле `Provider` заменить на dropdown из этого справочника.

Выбранный подход: заменить текущий свободный строковый `Provider` на связь модели с `provider_id`, а существующие значения `model_prices.provider` мигрировать в новый справочник.

## Key Changes

- В Billing добавить таблицу `billing.model_providers`: `id`, `display_name`, `description_ru`, `description_en`, `logo_bytes`, `logo_content_type`, timestamps.
- В `billing.model_prices` добавить nullable `provider_id`; миграцией создать провайдеров из distinct старых `provider` и заполнить `provider_id`.
- Старое поле `model_prices.provider` оставить как fallback/backcompat, но источником истины сделать `provider_id`.
- Добавить доменную сущность `ModelProvider`, Dapper persistence, in-memory repository и методы:
  - `ListModelProviders`
  - `GetModelProvider`
  - `UpsertModelProvider`
  - `GetModelProviderLogo`
- В gRPC/proto добавить provider DTO и `provider_id` в model detail/upsert messages, сохранив текущий `provider` как display name для существующего UI/API.
- В WebApp добавить `ProviderInfo`, методы в `BillingProvider`, страницу `/admin/providers` по паттерну `/admin/tags`.
- Логотип загружать через multipart form, хранить в Billing DB как bytea. Разрешить PNG/JPEG/WebP, max 256 KiB; SVG не принимать в v1 из-за XSS-рисков.
- В `/admin/models` заменить input `Form.Provider` на select `Form.ProviderId`; оставить пустой вариант `- no provider -`.
- При OpenRouter enrich пытаться сопоставить provider name с существующим provider по `id`/`display_name`; если не найден, не создавать автоматически, а показать warning.

## UI Behavior

- В левое меню admin добавить пункт `Providers`.
- `/admin/providers?action=add|edit&id=...` показывает форму:
  - Provider ID, Display Name
  - Logo upload, current logo preview, checkbox/action для удаления логотипа
  - Description RU, Description EN
- В списке провайдеров показывать id, name, миниатюру логотипа, наличие RU/EN описания.
- Удаление провайдера в v1 не добавлять, так как запрос только про добавление и редактирование.

## Test Plan

- Billing unit tests: provider validation, create/update, logo keep/replace/clear, model provider_id roundtrip.
- Postgres repository tests: миграция/сохранение provider, logo bytea, list model returns provider display name through provider_id.
- gRPC tests: new methods, auth policy for `UpsertModelProvider`, public read methods, provider_id in model upsert/list.
- WebApp tests: admin providers page renders, save rejects invalid logo type/size, model edit shows provider dropdown.
- Проверка:
  - `dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1`
  - `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`
  - `dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1`

## Assumptions

- "Страница провайдера" в этой итерации означает админскую страницу `/admin/providers`, без публичной `/providers/{id}`.
- "Заменить" трактую как мигрировать старый строковый `Provider` в новый справочник и дальше выбирать провайдера из dropdown.
- Логотип должен переживать redeploy и работу нескольких WebApp replicas, поэтому хранится в Billing DB, а не в файловой системе WebApp.
