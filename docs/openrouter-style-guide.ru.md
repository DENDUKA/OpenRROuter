# OpenRouter Style Guide

Цель: темный, плотный, инженерный SaaS/UI. Минимум декора. Акцент на контент, таблицы, API, модели.

## Общий стиль

- Theme: dark-first.
- Mood: dev-tool, marketplace, API-console.
- Фон: почти черный, не чистый black.
- Поверхности: слабые серые панели, без ярких карточек.
- Контраст: белый текст + серые secondary labels.
- Радиусы: маленькие/средние, pill только для кнопок, tabs, badges.
- Layout: много whitespace, но сами таблицы плотные.
- Декор: линии, hairline borders, subtle hover. Без heavy shadows.

## Цвета

```css
:root {
  --bg: #0a0a0a;
  --surface: #111113;
  --surface-2: #17181a;
  --surface-3: #1f2023;

  --border: #26272b;
  --border-soft: #1b1c20;

  --text: #f5f5f5;
  --text-muted: #a1a1aa;
  --text-dim: #71717a;

  --accent: #ffffff;
  --accent-text: #0a0a0a;

  --green: #22c55e;
  --red: #ef4444;
  --yellow: #eab308;
  --blue: #60a5fa;
}
```

## Типографика

- Font: `Inter`, `Geist Sans`, system sans.
- Mono: `Geist Mono`, `SFMono-Regular`, `Menlo`.
- Weight: mostly `400`, rarely `500/600`.
- Заголовки не жирные. OpenRouter выглядит за счет размера и spacing, не bold.

```css
body {
  font-family: Inter, system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
  font-size: 14px;
  line-height: 1.5;
  color: var(--text);
  background: var(--bg);
}

h1 {
  font-size: 40px;
  line-height: 1.05;
  font-weight: 400;
  letter-spacing: -0.04em;
}

h2 {
  font-size: 28px;
  line-height: 1.15;
  font-weight: 400;
  letter-spacing: -0.03em;
}

h3 {
  font-size: 18px;
  line-height: 1.35;
  font-weight: 500;
}

.small {
  font-size: 12px;
  color: var(--text-muted);
}

.mono {
  font-family: "Geist Mono", ui-monospace, monospace;
}
```

## Сетка

- Desktop: left sidebar + content.
- Sidebar: fixed/sticky слева.
- Content: max-width `1120-1280px`.
- Main padding: `24-32px`.
- Внутренние gaps: `16px`, `24px`, `32px`.

```css
.app-shell {
  display: grid;
  grid-template-columns: 240px minmax(0, 1fr);
  min-height: 100vh;
}

.main {
  min-width: 0;
  padding: 24px 32px;
}
```

## Левая панель

OpenRouter sidebar ощущается как product navigation:

- Узкая, спокойная.
- Верх: logo/wordmark.
- Потом nav groups.
- Active item: светлый text + subtle bg.
- Items: icon + label, 32-36px height.
- Border справа: `1px solid`.
- Sticky, full-height.
- Scroll внутри при переполнении.
- На mobile: drawer/offcanvas.

```css
.sidebar {
  position: sticky;
  top: 0;
  height: 100vh;
  overflow-y: auto;
  background: var(--bg);
  border-right: 1px solid var(--border-soft);
  padding: 16px 12px;
}

.sidebar-logo {
  height: 40px;
  display: flex;
  align-items: center;
  padding: 0 10px;
  margin-bottom: 16px;
  font-size: 14px;
  letter-spacing: -0.01em;
}

.nav-section {
  margin: 18px 0;
}

.nav-section-title {
  padding: 0 10px 8px;
  font-size: 11px;
  color: var(--text-dim);
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.nav-item {
  height: 34px;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 10px;
  border-radius: 8px;
  color: var(--text-muted);
  font-size: 14px;
  text-decoration: none;
}

.nav-item:hover {
  color: var(--text);
  background: var(--surface);
}

.nav-item.active {
  color: var(--text);
  background: var(--surface-2);
}

.nav-icon {
  width: 16px;
  height: 16px;
  color: var(--text-dim);
}
```

## Top Nav

Если есть верхнее меню:

- Height: `56-64px`.
- Border-bottom.
- Links plain, 14px.
- CTA button справа.
- Header не должен быть тяжелым.

```css
.topbar {
  height: 56px;
  border-bottom: 1px solid var(--border-soft);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  background: rgba(10, 10, 10, .88);
  backdrop-filter: blur(12px);
}
```

## Кнопки

OpenRouter использует строгую иерархию из четырёх уровней кнопок. Принцип: форма важна для глобальных CTA, иконки — для строчных действий в таблицах.

### Уровни кнопок

| Вариант | Класс | Использование |
|---|---|---|
| **Primary** | `.btn .btn-primary` | Главное действие формы/модала: Create, Save, Done |
| **Primary sm** | `.btn .btn-sm .btn-primary` | Кнопка создания в заголовке панели: Create Key, Add Model |
| **Secondary** | `.btn .btn-secondary` | Отмена, второстепенное: Cancel, Back |
| **Danger** | `.btn .btn-danger` | Деструктивное подтверждение в модале: Revoke, Delete |
| **Icon** | `.btn-icon` | Редактирование строки таблицы (иконка карандаша) |
| **Icon danger** | `.btn-icon .btn-icon-danger` | Удаление/отзыв строки таблицы (иконка корзины) |

### Анатомия базовой кнопки

```css
.btn {
  min-height: 44px;       /* форм-фактор: 44px обычная, 36px sm */
  padding: 0 14px;
  border-radius: 999px;   /* pill — всегда для .btn */
  border: 1px solid var(--border);
  background: transparent;
  color: var(--text);
  font-size: 14px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  transition: background .15s ease, border-color .15s ease, color .15s ease;
}

/* Primary: solid white, dark text */
.btn-primary {
  background: var(--text);           /* #f5f5f5 */
  color: var(--bg);                  /* #0a0a0a */
  border-color: var(--text);
}
.btn-primary:hover {
  background: #ffffff;
  border-color: #ffffff;
}

/* Secondary: ghost с рамкой */
.btn-secondary {
  background: transparent;
  color: var(--text);
  border-color: var(--border);       /* #26272b */
}
.btn-secondary:hover {
  background: var(--surface-2);
  border-color: #3f4045;
}

/* Danger: красный цвет, прозрачный фон */
.btn-danger {
  color: var(--red);                 /* #ef4444 */
  border-color: rgba(239, 68, 68, .45);
  background: transparent;
}
.btn-danger:hover {
  background: rgba(239, 68, 68, .1);
  border-color: rgba(239, 68, 68, .65);
}

/* Small modifier */
.btn-sm {
  min-height: 36px;
  padding: 0 12px;
  font-size: 13px;
}
```

### Icon-only кнопка (строки таблиц)

OpenRouter использует иконки без текста для row-level действий edit/delete. Кнопка квадратная, без рамки, прозрачная. Иконка серая в покое, белая при hover. Деструктивная иконка становится красной при hover.

```css
.btn-icon {
  width: 28px;
  min-height: 28px;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--text-dim);            /* #71717a — серая иконка */
  border-radius: 8px;                /* radius-sm, НЕ pill */
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: color .15s ease, background .15s ease;
}

.btn-icon:hover {
  color: var(--text);                /* #f5f5f5 */
  background: var(--surface-3);     /* #1f2023 */
}

/* Деструктивный hover: иконка красная */
.btn-icon-danger:hover {
  color: var(--red);                 /* #ef4444 */
  background: rgba(239, 68, 68, .1);
}
```

### Иконки для действий

Inline SVG, stroke-based, 14×14, `stroke="currentColor"`:

- **Edit / Rename** — карандаш (`M11 4H4a2 2 0 0 0-2 2v14... / M18.5 2.5...`)
- **Delete / Revoke** — корзина (`polyline 3 6 5 6 21 6 / path d="M19 6v14..."`)
- **Disable / Off** — перечёркнутый круг (`circle cx=12 cy=12 r=10 / line x1=4.93 y1=4.93 x2=19.07 y2=19.07`)
- **Top Up / Add** — плюс (`line x1=12 y1=5 x2=12 y2=19 / line x1=5 y1=12 x2=19 y2=12`)

### Применение в контексте

```html
<!-- Заголовок панели: btn-sm btn-primary -->
<div class="panel-header d-flex justify-content-between align-items-center">
  <span>API Keys</span>
  <button class="btn btn-sm btn-primary">Create Key</button>
</div>

<!-- Строка таблицы: icon buttons -->
<td class="actions-cell">
  <button class="btn-icon" title="Rename" aria-label="Rename">
    <!-- pencil svg 14×14 -->
  </button>
  <button class="btn-icon btn-icon-danger" title="Revoke" aria-label="Revoke">
    <!-- trash svg 14×14 -->
  </button>
</td>

<!-- Форма/модал: full-size primary + secondary -->
<div class="btn-row">
  <button class="btn btn-primary">Save</button>
  <button class="btn btn-secondary">Cancel</button>
</div>

<!-- Деструктивный модал: danger + secondary -->
<div class="btn-row">
  <button class="btn btn-danger">Revoke</button>
  <button class="btn btn-secondary">Cancel</button>
</div>
```

### Спиннер загрузки

Все асинхронные кнопки добавляют спиннер и `disabled` во время операции:

```html
<button class="btn btn-primary" disabled="@IsSaving">
  @if (IsSaving) { <span class="spinner-border-sm me-2"></span> }
  Save
</button>
```

### Что НЕ делать

- Не использовать текстовые `btn-sm` кнопки (Rename, Edit, Delete) в строках таблиц — только иконки
- Не смешивать `btn-outline-primary` / `btn-outline-danger` — их нет в дизайн-системе
- Не делать pill-форму у icon-button — только `border-radius: 8px`
- `btn-primary` solid только для главного действия; в panel header — `btn-sm btn-primary`

## Таблицы `/models`

Ключ: таблица не выглядит как spreadsheet. Она как searchable catalog.

Структура:

- Верх страницы: title `Models`, справа actions `Compare`.
- Под ним filter/search row.
- Таблица занимает всю ширину.
- Header muted, small.
- Rows 56-72px.
- Hover row: subtle surface.
- Модель: иконка provider + name + slug/description.
- Числа выровнены вправо.
- Tags/badges для features.
- Sticky table header желательно.
- Горизонтальный scroll на mobile.

```css
.table-wrap {
  border: 1px solid var(--border-soft);
  border-radius: 12px;
  overflow: hidden;
  background: var(--bg);
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

.table thead {
  background: var(--bg);
}

.table th {
  height: 40px;
  padding: 0 14px;
  text-align: left;
  color: var(--text-dim);
  font-size: 12px;
  font-weight: 400;
  border-bottom: 1px solid var(--border-soft);
  white-space: nowrap;
}

.table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--border-soft);
  vertical-align: middle;
}

.table tr:hover td {
  background: var(--surface);
}

.table .num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.model-cell {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 280px;
}

.provider-icon {
  width: 28px;
  height: 28px;
  border-radius: 7px;
  background: var(--surface-2);
}

.model-name {
  color: var(--text);
  font-weight: 500;
}

.model-slug {
  color: var(--text-dim);
  font-size: 12px;
  font-family: "Geist Mono", monospace;
}
```

## Filters/Search

- Search input dark, border, rounded.
- Filters as compact pills.
- Active filter: white/bright border or filled surface.
- `Filter` button mobile/desktop.

```css
.filter-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 20px 0;
}

.search {
  height: 38px;
  min-width: 280px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text);
  padding: 0 14px;
}

.filter-pill {
  height: 32px;
  padding: 0 12px;
  border-radius: 999px;
  border: 1px solid var(--border);
  color: var(--text-muted);
  background: transparent;
}
```

## Badges

- Маленькие, neutral.
- Без ярких заливок.
- Feature badges: `Tools`, `Vision`, `Reasoning`, `Free`, `Fast`.

```css
.badge {
  display: inline-flex;
  height: 22px;
  align-items: center;
  padding: 0 8px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text-muted);
  font-size: 12px;
}
```

## Страница модели

OpenRouter model page structure:

- Provider icon.
- H1: `Anthropic: Claude Opus 4.7 (Fast)`.
- Subtitle slug: `anthropic/claude-opus-4.7-fast`, mono/muted.
- Actions: `Chat`, `Compare`.
- Description paragraph.
- Metrics row/cards:
- Modalities.
- Input Price.
- Output Price.
- Context.
- Weekly Tokens.
- Released.
- Tabs:
- Overview.
- Playground.
- Providers.
- Performance.
- Pricing.
- Apps.
- Activity.
- Uptime.
- API.
- Content below: section title + explanation/table.

```css
.model-hero {
  max-width: 980px;
  padding: 32px 0 20px;
}

.model-provider-icon {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  margin-bottom: 18px;
}

.model-title {
  margin: 0;
  font-size: 36px;
  font-weight: 400;
  letter-spacing: -0.04em;
}

.model-slug-line {
  margin-top: 8px;
  color: var(--text-muted);
  font-size: 16px;
}

.model-desc {
  max-width: 780px;
  margin-top: 20px;
  color: var(--text-muted);
  font-size: 15px;
}
```

## Model Metrics

OpenRouter style: метрики простые, label сверху, value снизу. Не heavy карточки.

```css
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 18px;
  margin: 28px 0;
}

.metric {
  min-width: 0;
}

.metric-label {
  color: var(--text-dim);
  font-size: 12px;
  margin-bottom: 4px;
}

.metric-value {
  color: var(--text);
  font-size: 16px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.metric-value .unit {
  color: var(--text-dim);
  font-size: 12px;
  margin-left: 2px;
}
```

## Tabs

- Horizontal.
- Border-bottom.
- Active tab: text white + bottom border.
- Scrollable на mobile.

```css
.tabs {
  display: flex;
  gap: 2px;
  border-bottom: 1px solid var(--border-soft);
  overflow-x: auto;
}

.tab {
  height: 42px;
  padding: 0 14px;
  display: inline-flex;
  align-items: center;
  color: var(--text-muted);
  border-bottom: 1px solid transparent;
  white-space: nowrap;
  font-size: 14px;
}

.tab:hover {
  color: var(--text);
}

.tab.active {
  color: var(--text);
  border-bottom-color: var(--text);
}
```

## Cards

На главной OpenRouter карточки:

- Border.
- Dark background.
- Rounded `12-16px`.
- Large visual/icon сверху.
- Title 18-20.
- Text muted.
- Hover: border brighter, bg slightly lighter.

```css
.card {
  border: 1px solid var(--border-soft);
  background: var(--surface);
  border-radius: 14px;
  padding: 20px;
}

.card:hover {
  border-color: var(--border);
  background: var(--surface-2);
}
```

## Homepage Pattern

- Hero center/left:
- Huge title.
- Muted subtitle.
- CTA pair.
- Stats row.
- Feature cards grid.
- Featured models/apps cards.
- Footer multi-column.

Hero should feel:

- Quiet.
- Large type.
- Very restrained color.
- Logo/provider icon clusters as visual interest.

## Forms

- Inputs dark surface.
- Border subtle.
- Focus border brighter.
- Placeholder dim.
- Labels small/muted.

```css
.input {
  height: 38px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  color: var(--text);
  padding: 0 12px;
}

.input:focus {
  outline: none;
  border-color: #52525b;
}
```

## Pricing Display

Use tabular numbers:

```css
.price {
  font-variant-numeric: tabular-nums;
  font-feature-settings: "tnum";
}

.price-main {
  font-size: 18px;
  color: var(--text);
}

.price-unit {
  font-size: 12px;
  color: var(--text-dim);
}
```

## Links

- Normal links white or muted.
- Inline links underline only on hover.
- External docs links can be muted blue/white.

```css
a {
  color: var(--text);
  text-decoration: none;
}

a:hover {
  text-decoration: underline;
  text-underline-offset: 3px;
}
```

## Responsive

```css
@media (max-width: 900px) {
  .app-shell {
    grid-template-columns: 1fr;
  }

  .sidebar {
    display: none;
  }

  .main {
    padding: 20px 16px;
  }

  .metrics-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .table-scroll {
    overflow-x: auto;
  }
}
```

## Что копировать с OpenRouter

- Dark minimal SaaS.
- Left nav с grouped links.
- Очень тонкие borders.
- Таблицы как catalog, не admin panel.
- Model page: hero + metrics + tabs + content.
- Mono slugs/model IDs.
- Compact buttons/pills.
- Muted secondary text.
- White primary CTA.

## Что не копировать буквально

- Лого/бренд OpenRouter.
- Exact layout/assets/icons.
- Тексты/названия/данные.
- Уникальные иллюстрации/графики.

## Компоненты для нашего сайта

1. `AppShell`: sidebar + main.
2. `SidebarNav`: groups/items/active state.
3. `PageHeader`: title/subtitle/actions.
4. `FilterBar`: search + filter pills.
5. `DataTable`: compact catalog table.
6. `ModelCell`: icon/name/slug.
7. `MetricGrid`: label/value stats.
8. `Tabs`: model subpages.
9. `Badge`: features/status.
10. `CardGrid`: homepage/features/apps.

## Итог

Дизайн должен выглядеть как `OpenRouter-like dark AI model marketplace`, но с нашим брендом. Основа: черный фон, белый primary CTA, muted серые тексты, плотные таблицы, sticky left sidebar, model detail page через hero + stats + tabs.
