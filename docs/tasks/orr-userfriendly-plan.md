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

### E. Setup guide (эндпоинты)

**Что.** Пункт меню `/docs` («Setup guide») **уже есть** в навбаре `_Layout.cshtml:48` — нужна сама страница.

Содержимое: Base URL, Authentication (`Authorization: Bearer <key>`), `POST /v1/chat/completions`
(curl + JSON пример), `GET /v1/models`, ссылка «Get API Key» (`/workspace/apikeys`).
Везде кнопки-копии (после A работают).

**Открытое решение (отложено юзером).** На `/models` и детали модели — **блок** про эндпоинты ИЛИ **ссылка** на Setup guide.
Рекомендация: сначала ссылка (дёшево, обратимо), инлайн-блок — вторым заходом если понадобится.

**Источник Base URL.** Не хардкодить — взять из конфига (ApiGateway public URL). Проверить, есть ли уже
конфиг с публичным URL гейтвея (`ApiGatewayUrl` named HttpClient есть, но это внутренний адрес — нужен внешний public).
