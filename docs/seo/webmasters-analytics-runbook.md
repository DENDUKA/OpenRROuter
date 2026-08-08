# Runbook: подключение вебмастеров и аналитики (SEO-002)

Подключение `openrussiarouter.com` к Google Search Console, Яндекс Вебмастеру, Яндекс Метрике
и Google Analytics 4. Итоговый критерий: домен подтверждён в обеих системах, счётчики отдают
события конверсии (регистрация → подтверждение email → создание API-ключа → первое пополнение).

## Разделение ответственности

| Шаг | Кто |
|---|---|
| DNS-подтверждение, аккаунты, счётчики, связки, цели | человек (владелец аккаунтов) |
| Вставка тегов в layout, CSP-хосты, конверсионные события, тесты | агент (код) |

## 0. Предусловия

- Прод задеплоен, `https://openrussiarouter.com` отвечает, `/robots.txt` и `/sitemap.xml` отдают 200.
- Доступ к DNS-панели `openrussiarouter.com`.
- Аккаунты Google (Search Console + GA4) и Яндекс (Вебмастер + Метрика).

## 1. Google Search Console (руками)

1. Search Console → «Добавить ресурс» → тип **«Домен»** (`openrussiarouter.com`).
2. Способ подтверждения → **DNS-запись**, скопировать `google-site-verification=...`.
3. Добавить эту **TXT-запись** в DNS (пропагация обычно ≤ 1ч).
4. Подтвердить в консоли.

## 2. Яндекс Вебмастер (руками)

1. Добавить сайт `openrussiarouter.com` → «Подтверждение прав».
2. Способ → **DNS-запись**: TXT `yandex-verification: <hex>`.
3. Добавить в DNS, подтвердить.

## 3. Google Analytics 4 (руками)

1. Создать GA4-свойство → Web data stream на `https://openrussiarouter.com`.
2. Получить **Measurement ID** `G-XXXXXXX`.
3. (опционально) Enhanced Measurement.

## 4. Яндекс Метрика (руками)

1. Создать счётчик (тип «Сайт»), домен `openrussiarouter.com`.
2. Получить **номер счётчика**.
3. В настройках: включить **анонимизацию IP**; Webvisor — на усмотрение (пишет видео-сессии,
   конфликтует с privacy-позой сервиса «визиты без кук и raw IP», в коде включён
   `webvisor:true` в init — при необходимости выключить).

## 5. Связка GSC ↔ GA4 (руками)

GA4 → Admin → Property → **Search Console links** → добавить GSC-ресурс.

## 6. Внедрение кода (агент)

- `AnalyticsOptions` (`GoogleTagId`, `YandexCounterId`), секция `Analytics` в `appsettings.json`.
- `_Layout.cshtml`: gtag.js + счётчик Метрики в `<head>`, gated по непустым ID;
  конверсии через `ViewData["AnalyticsEvents"]`.
- `Program.cs`: CSP расширяется **условно** — `googletagmanager.com`/`google-analytics.com`/
  `analytics.google.com` при `GoogleTagId`, набор хостов Метрики при `YandexCounterId`:
  `mc.yandex.ru`, `mc.yandex.com` (cookie-sync пиксель + `wss://mc.yandex.com/solid.ws`),
  `mc.webvisor.org`/`mc.webvisor.com`, `yastatic.net`; `wss:`-схема в `connect-src`
  перечисляется отдельно от `https:`.
- Прод-значения — в `deploy/compose/compose.prod.yml` (`Analytics__GoogleTagId`,
  `Analytics__YandexCounterId`).
- Тесты в `SeoHeadTests`: без конфига тегов нет; с конфигом — оба сниппета на месте.

### Конверсионные события

| Цель | Событие | Где | Механизм |
|---|---|---|---|
| Регистрация | `signup` | `/verify-email` после успешного signup | `TempData["AnalyticsSignup"]` |
| Подтверждение email | `email_verified` | `/workspace/apikeys` после верификации | `TempData["AnalyticsEmailVerified"]` |
| Создание API-ключа | `api_key_created` | `/workspace/apikeys` после создания | `TempData["AnalyticsApiKeyCreated"]` |
| Первое пополнение | `topup_completed` | `/workspace/credits?topup=success` | query-флаг |

«Первое» в GA4/Метрике считается по distinct user в отчётах — клиент не знает «первое ли».

## 7. Настройка целей (руками, после деплоя)

Код уже шлёт события в оба счётчика автоматически (`gtag('event', …)` + `ym(…,'reachGoal',…)`),
в кабинетах их нужно только **объявить целями/конверсиями**. События придут после деплоя кода —
до этого теги не активны, настройка целей впрок бессмысленна.

### 7.1 Google Analytics 4 — пометить события как conversions

1. GA4 → **Admin** → колонка Property → **Conversions**.
2. **New conversion event** → по одному имени: `signup`, `email_verified`,
   `api_key_created`, `topup_completed`.
3. Сохранить. Появятся в отчётах Conversions и в главном табло «Конверсии».

Опционально:
- **Admin → Property → Events**: события появятся сами после первого попадания данных
  (проверить, что имя точное — регистр важен).
- **Explorations / Reports**: строить воронку `signup → email_verified → api_key_created →
  topup_completed` по событиям.
- Связка с Search Console — шаг 5.

### 7.2 Яндекс Метрика — объявить цели

`ym(111420552, 'reachGoal', …)` отправляет события через «Достижение целей». Метрика сама
заведёт события с именами `signup` и т.д., но цель как конверсию лучше объявить явно:

1. Счётчик → **Настройки → Цели** (или «Цели» в левом меню).
2. **Добавить цель** → тип **«JavaScript-событие»**.
3. В поле **ID цели** указать имя события по одному: `signup`, `email_verified`,
   `api_key_created`, `topup_completed`.
4. Сохранить. Каждый `reachGoal('signup')` станет достижением цели.

Проверка: Метрика → **Realtime** → «Цели» — должны появляться после тестовых действий;
отчёт «Конверсии» покажет воронку.

### 7.3 Что учесть

- **«Первое пополнение»** (`topup_completed`) — в обоих системах это просто «пополнение»;
  «первое» выводится сегментацией по пользователю в отчётах, а не настройкой цели.
- События не дублируются: `TempData`-флаги одноразовые, повторное обновление страницы
  не триггерит повторно.
- `signup` шлётся только из реальной регистрации (не из логина) — см. `Signup`/`VerifyEmail`.

## 8. Проверка

- Метрика: «Проверить код», Realtime; GA4: DebugView/Realtime.
- Тест-конверсия на проде: регистрация → верификация → создание ключа → тест-пополнение.
- Sitemap/переобход — задачи SEO-020/SEO-021.

## ⚠️ Юридическая точка (уточнить у продукта)

Метрика и GA4 ставят **cookies** — положение по 152-ФЗ меняется относительно нынешнего «визиты
без кук» (`SiteVisitTrackingMiddleware`). Дополнить `docs/mandatory/privacy-policy.md`; возможно,
понадобится consent-баннер — его в коде нет.
