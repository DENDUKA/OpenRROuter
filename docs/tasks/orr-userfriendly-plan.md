# ORR → user-friendly: план

Цель: привести user-facing часть WebApp (Razor Pages) в нормальный вид. Разбито на 5 блоков (A–E). **A+B+C сделаны в первом заходе**, D+E — этот файл, для свежего чата.

## Контекст / диагноз (на момент старта)

Затрагивается только `src/OpenRussiaRouter.WebApp` (Razor Pages). 9RFork — отдельный фронт, не трогаем.

Ключевой факт про layout-ы: `_AdminLayout.cshtml` рендерится **внутри** `_Layout.cshtml` (`Layout = ".../_Layout.cshtml"`), поэтому глобальный `<script>` из `_Layout` доступен и в админке.

## Сделано (A+B+C)

- **A. Копирование.** В `_Layout.cshtml` добавлен общий `orrCopyText(text)` с фолбэком `navigator.clipboard` → `execCommand('copy')` (clipboard API недоступен в insecure context, а оба деплоя — HTTP). `copyModelId` переписан на него; дубль-функция удалена из публичного `Pages/Models.cshtml`; `copyApiKey` в `Pages/Workspace/ApiKeys.cshtml` переведён на `orrCopyText`.
- **B. View моделей.** На `/models` под именем модели выводится `id` моноширинным (класс `.model-slug`) + рабочая кнопка-копия.
- **C. Фейк-providers.** Убраны: колонка Providers в таблице `/models`; секция+таб Providers на `ModelDetails.cshtml`.

## Осталось

### D. Чистка описаний моделей

**Что.** `Description` тянется из OpenRouter as-is (только `.Trim()`) в
`Services/Enrichment/OpenRouterCatalogClient.cs:109` (`FindMatch` → `OpenRouterMatchResult`).
Показывается на `ModelDetails.cshtml` (Overview, `@Model.Description`). В публичной таблице описания нет.
Это markdown автора модели: ссылки `[label](url)`, маркетинг, хвостовые ноты про варианты/провайдеров/роутинг.

**Подход (согласован).** Санитайзер:
1. markdown → плоский текст (срезать `[label](url)` → `label`, убрать `#`/`*`/`>` разметку);
2. отрезать хвостовые boilerplate-ноты про варианты/провайдеров/роутинг.

Применять на этапе энрича (в `FindMatch`, перед возвратом `Description`), чтобы при ре-энриче хлам не отрастал. Плюс display-guard на `ModelDetails` для уже сохранённых в БД строк.

**Блокер.** Точные правила «что резать» настроить на **реальных 2–3 примерах**, а не на угад (иначе недочистка / съедание полезного текста). Описания живут в БД биллинга на живом деплое.
Способы достать примеры: вытащить с кластера (см. топологию деплоя в memory) ИЛИ юзер кидает 2–3 примера «вот это хлам».

**Тесты.** Рядом с `OpenRouterCatalogClient` логика тестируема — добавить unit-тесты санитайзера на собранных примерах.

### E. Setup guide (эндпоинты) — СДЕЛАНО во втором заходе

Страница `Docs.cshtml` (`/docs`) уже существовала. Добавлено:
- code-behind `Docs.cshtml.cs` — резолвит `ApiBaseUrl` из конфига `PublicApiBaseUrl`, фолбэк на request origin.
- Секция **Endpoint**: Base URL (`{base}/v1`) с кнопкой-копией, Auth header, таблица методов (`POST /v1/chat/completions`, `GET /v1/models`).
- Исправлен curl: реальный `@Model.ChatCompletionsUrl` вместо `curl /v1/...`.
- Ключ `PublicApiBaseUrl` добавлен в `appsettings.json` (пустой).

**Base URL теперь правится из админки** (Admin → Index → «Platform settings»), не только из конфига.
Реализован persist-стор `billing.platform_settings` (KV): миграция `015`, proto `Get/SetPlatformSetting`,
репозиторий (PG+in-memory), фасад, gRPC, `BillingProvider`, ключ `PlatformSettingKeys.PublicApiBaseUrl`.
Цепочка в Docs: **admin-настройка → конфиг `PublicApiBaseUrl` → request origin**.

**⚠️ Деплой-действие (ops).** На dev VPS WebApp = :3000, gateway = :8080 → задать base URL
(`http://31.76.49.2:8080`) **в админке** (или конфигом). Без этого гайд покажет origin кабинета (:3000) — неверно.
На kind (всё за одним ingress) фолбэк сам даёт правильный `http://<host>/v1`.

**Открытое решение (всё ещё отложено).** На `/models` / детали — инлайн-**блок** про эндпоинт ИЛИ **ссылка** на `/docs`.
Пока ничего не добавлено (в топбаре есть пункт «Setup guide» → `/docs`). Рекомендация прежняя: ссылка, потом блок при желании.

### Доп. правки второго захода (не из исходных A–E)

- Каталог `/models`: добавлена узкая колонка **Author** (`m.Provider`) — по просьбе вернуть разработчика/владельца, подписан корректно.
- **Переключатель валюты** (₽/$/€) в топбар-дропдауне: `Controllers/PreferencesController.cs` (`POST /preferences/currency`),
  пишет в профиль (`displayCurrency`) + куку `orr_ccy` для подсветки активной. Settings-страница тоже ставит куку (синк). CSS — `.ccy-*` в `app.css`.
