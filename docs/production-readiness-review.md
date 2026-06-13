# Production readiness review

_Дата: 2026-06-13 (актуализация ревью от 2026-06-12)_
_Модель: Claude Opus 4.8_
_Источники: README.md, CLAUDE.md, TODO.md, docs/todo.md, docs/tasks/orr-userfriendly-plan.md, docs/billing-payments-implementation-plan.md, docs/email-integration-plan.md + сверка по коду (платежи, rate limiting, восстановление пароля, lockout, метрики, TLS, секреты)._

## Вердикт

Ядро MVP в хорошем состоянии: весь пайплайн gateway → auth → billing → control plane → 9Router работает, включая streaming, retry, идемпотентный ledger, синк ключей на ноды, signup с email-подтверждением, OpenTelemetry-метрики. С прошлого ревью закрыты два блокера — восстановление/смена пароля и lockout логина (см. ниже). Но до «продакшена с реальными пользователями и реальными деньгами» остаётся несколько блокеров, и почти все они одновременно бьют по юзабилити.

## Закрыто с 2026-06-12

- **Восстановление и смена пароля** (коммит `dcb31d8`). `ChangePassword` в `auth.proto`, флоу `/forgot-password` → 6-значный код на почту → `/reset-password`, смена пароля в `/workspace/settings`. Таблица `auth.password_resets`, миграция 011. Бывший блокер №3.
- **Lockout логина** (тот же коммит). 5 неудачных попыток → блокировка на 15 мин; поля `failed_login_count`/`locked_until_utc` в `auth.users`. Закрывает половину бывшего блокера №4 (вторая половина — rate limiting на Gateway — ещё открыта).
- **Платёжный каркас**. Реализованы `IPaymentProvider`, `PaymentCheckoutService`, webhook-endpoint с verify + per-IP rate limiting, `PaymentReconciliationWorker`, `PaymentStartupGuard`, страница `/workspace/payment-success`. Осталось написать один конкретный адаптер (см. блокер №1) — каркас полностью готов его принять.
- **Rate limiting на Gateway** (2026-06-13). Per-API-key (фолбэк per-IP) fixed-window лимитер на `POST /v1/chat/completions[/stream]` — встроенный `Microsoft.AspNetCore.RateLimiting`, конфиг `RateLimiting` (`Enabled`/`PermitLimit`/`WindowSeconds`, дефолт 60/мин), отказ в OpenAI-формате (`429` + `Retry-After`, `code=rate_limited`). Покрыт интеграционными тестами. Бывший блокер №3.
- **Предохранитель retry-цикла Gateway** (2026-06-13, попутно). Найден и закрыт предсуществующий баг: `ChatCompletionHandler` на 5xx ретраил ноды в `while(true)` без потолка — при ControlPlane, вернувшем ноду из excluded-набора, это бесконечный цикл с пегом CPU (он же давал зависающий тест и красный CI). Добавлен cap `MaxRouterAttempts=5` в оба цикла (non-stream + stream). Весь набор тестов Gateway (71) зелёный.
- **Rollypay-адаптер написан** (2026-06-13). Реализован `RollypayPaymentProvider : IPaymentProvider` поверх готового каркаса: тонкий `RollypayClient` (X-API-Key + per-request `X-Nonce`, маппинг 4xx→`RollypayApiException` / 5xx·429·таймаут→`RollypayUnavailableException`), HMAC-SHA256-верификатор вебхука (`{X-Timestamp}.{body}`, скос 5 мин), маппинг event/status, провайдер шлёт в Rollypay **ровно** записанные Billing-ом amount/currency (никакого override валюты — иначе зачислили бы одно, списали другое). DI: named `HttpClient` "Rollypay" + регистрация безусловная (активен только при `ActiveProvider=rollypay`, иначе fail-safe). Тесты: golden-HMAC + адаптер (CreateAsync/Parse/Verify/GetPayment через стаб) — весь WebApp.Tests (97) зелёный. Закрывает кодовую часть бывшего блокера №1; до боевого включения осталось не-кодовое (см. ниже).

## Блокеры для продакшена

### 1. Реальные платежи: код есть, не-кодовое включение остаётся

Код закрыт (см. «Закрыто», 2026-06-13): `RollypayPaymentProvider` написан и покрыт тестами. До того, как пользователь сможет реально пополнить баланс, осталось **не-кодовое**:

- завести кассу в Rollypay, получить `ApiKey` + `SigningSecret`, положить их в прод-секрет (НЕ в git) и выставить `Payments:ActiveProvider=rollypay`;
- прописать webhook-URL `https://<домен>/webhook/rollypay` в UI кассы — **зависит от блокера №2 (HTTPS)**: без домена/TLS вебхук некуда направить;
- **checkout должен форсить RUB** для rollypay-провайдера. Сейчас `PaymentCheckoutService` передаёт в провайдера display-валюту пользователя; провайдер шлёт её как есть. Для RUB-дефолта (основной путь) всё консистентно, но при display=USD/EUR Rollypay отклонит платёж (СБП — только рубли). Пока display по умолчанию RUB — это не блокер запуска, но до включения мультивалютного топ-апа надо форсить RUB на стороне checkout;
- боевой smoke-тест минимальной суммой (у Rollypay нет sandbox).

### 2. Нет HTTPS

Оба деплоя — голый HTTP по IP/`.local`. В kind ingress слушает `host: localhost`/`*.local` без `tls:`-секции и cert-manager; на VPS — HTTP по IP. Это уже ломало clipboard API (пришлось делать фолбэк в `_Layout.cshtml`), но главное — API-ключи `sk-or-*` и пароли ходят открытым текстом. Нужен домен + TLS (Caddy/Traefik на VPS, cert-manager на kind). Для LLM-гейтвея это не опция, а требование.

### 3. Надёжность денег

Из CLAUDE.md «Не готово»:

- usage events — fire-and-forget gRPC без outbox (упал Billing — потеряли списание);
- нет immutable price history (`billing.model_prices` — один active row);
- нет lease recovery после рестарта ControlPlane.

Outbox — самое важное из трёх: потерянный usage = потерянные деньги.

### 4. Ops-хвосты

- plain-text секреты в git (INFRA-003, SOPS+Age не сделан) — проверено: `postgres-secret.yaml`, `s2s-secret.yaml`, `f9router-2-secret.yaml` лежат в открытом виде в `infra/kustomize/base/`;
- SMTP-секрет на проде не запровижен — без него коды регистрации/восстановления пароля не уходят (а флоу восстановления теперь критически от них зависит);
- проверить, что CI зелёный: в конце мая он две недели был красным при «Synced» в ArgoCD (live-кластер крутил устаревшие образы).

## Юзабилити — «просто и наглядно»

### 1. Onboarding-чеклист на главной кабинета

Сейчас после регистрации пользователь попадает в пустой кабинет и должен сам догадаться: создать ключ → пополнить → найти base URL. Сделать на `/` (Workspace Index) блок «3 шага»: создать ключ / пополнить баланс / скопировать готовый curl с уже подставленным base URL. Страница `/docs` уже есть и base URL правится из админки — осталось вынести это на первый экран.

### 2. Playground — попробовать модель без кода

В админке уже есть `GatewayTest`; пользовательской версии нет. Простая страница «выбери модель → напиши сообщение → получи ответ» — самый наглядный способ показать, что сервис работает, до того как пользователь напишет хоть строчку кода. Сильно снижает порог входа.

### 3. Графики расходов (BILLING-006)

`/workspace/activity` — сырая таблица записей. Пользователю нужно «сколько я потратил за неделю и на какие модели» — агрегаты по дням/моделям + простой график на Credits/Activity. Задача давно в TODO, это главный недостающий кусок наглядности.

### 4. Предупреждение о низком балансе

Сейчас пользователь узнаёт о нулевом балансе по 402 из API. Баннер в кабинете + письмо при пересечении порога (SMTP уже есть).

### 5. Санитайзер описаний моделей

Доделать задачу D из `docs/tasks/orr-userfriendly-plan.md`: сейчас в каталог течёт сырой markdown с маркетингом из OpenRouter. План есть, нужны 2–3 реальных примера с прода для правил очистки.

### 6. Язык интерфейса

UI весь на английском, при этом продукт целится в русскоязычную аудиторию (RUB по умолчанию, СБП). Решить осознанно: либо русифицировать кабинет (не админку), либо оставить английский как «привычный для разработчиков» — сейчас это выглядит ненамеренным.

## Приоритет

```
HTTPS (домен + TLS)
  → включить Rollypay (касса/секреты + webhook-URL + ActiveProvider; код уже написан)
  → onboarding-чеклист + playground
  → billing outbox
  → графики расходов
```

~~восстановление пароля + lockout~~, ~~rate limiting на Gateway~~, ~~адаптер Rollypay (код)~~ — сделано (2026-06-12 → -13). Главная оставшаяся «дверь наружу» — **HTTPS**: он же разблокирует включение Rollypay (webhook-URL требует домена). После него — включение кассы и не-кодовые шаги из блокера №1. Следующие два пункта делают первый опыт понятным, остальное — надёжность и полировка.
