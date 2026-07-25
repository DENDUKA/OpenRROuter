# WebApp — CLAUDE.md

ASP.NET Core **Razor Pages** — кабинет пользователя и админ-дашборд. **HTTP (порт 5004)**, cookie-аутентификация. К Auth/Billing/ControlPlane/Collector ходит как gRPC-клиент; к ApiGateway и нодам 9Router — HTTP. Своей БД нет — вся логика в backend-сервисах.

## Bootstrap ([Program.cs](Program.cs))

- `AddSeqLogging("webapp")` + `AddOpenTelemetryMetrics` (`MapOpenTelemetryEndpoints`);
- **Cookie-auth**: основная схема `Cookies` (LoginPath `/Login`, 30 дней, sliding) + вторая схема `External` (`.orr.ext`) для OAuth;
- **Google-логин регистрируется условно** — только если заданы `Google:ClientId`/`Google:ClientSecret`. Без гварда OAuth-хендлер падает с 500 на `/Login` («ClientId option must be provided»);
- **gRPC-клиенты** Auth(5001)/Billing(5002)/ControlPlane(5003)/Collector(5005) — каждый scoped-провайдер, keep-alive + `GrpcDeadlineInterceptor` (`GrpcServices:CallDeadlineSeconds`, дефолт 30);
- **именованные `HttpClient`**: `ApiGateway` (`ApiGatewayUrl`, 120с), `RouterNode` (HTTP/1.1 exact, 20с), `DirectNodeChat` (HTTP/1.1, 120с — для UI-теста прокси);
- `AddEmailInfrastructure`;
- `AddProductAnalytics` + `AddSiteVisitTracking` (BuildingBlocks) — аналитическая БД `orr_analytics`
  (см. «Трекинг визитов» ниже).

## Структура

| Папка | Содержимое |
|---|---|
| `Pages/` | Razor Pages (`.cshtml` + `.cshtml.cs`). `PageModelBase` — общий базовый класс. Корень — workspace; `Pages/Admin/`, `Pages/Workspace/`, `Pages/Login/ExternalCallback` |
| `Services/Auth` | `AuthProvider` (gRPC→Auth) + DTO; `AuthException`; `ClaimsExtensions` |
| `Services/Billing` | `BillingProvider` (gRPC→Billing) + DTO (balance, usage, models, anomalies, tags); `BillingException` |
| `Services/ControlPlane` | `ControlPlaneProvider` (gRPC) + `RouterNodeApiProvider` (прямой HTTP к нодам, напр. `/v1/models`) |
| `Services/Collector` | `CollectorProvider` (gRPC→Collector) |
| `Services/Enrichment` | `OpenRouterCatalogClient` (`IOpenRouterCatalogClient`) — обогащение каталога моделей |
| `Services/State` | `UserSession`, `ClaimsExtensions`; `Services/ColorContrast.cs` |
| `Email/` | SMTP: `SmtpEmailSender` (`IEmailSender`), очередь `EmailDispatchQueue`+`EmailDispatchWorker`, `EmailTemplates`, `EmailBroadcastService`, `EmailSettings` |
| `Services/SiteStats` | `SiteVisitTrackingMiddleware` — запись визитов на сайт в `analytics.site_visits` |
| `Controllers/Admin` | `GatewayTestController` — прогон запроса через gateway/ноду из UI |

## Карта страниц

Полный список — в корневом [CLAUDE.md](../../CLAUDE.md) («WebApp Pages»). Кратко: `/login`, `/signup`→`/verify-email`, `/invite/{token}`, `/` (workspace index), `/workspace/{api-keys,activity,credits,settings}`, `/admin/{users,topups,models,nodes,nodes/{id},nodes/{id}/limits,anomalies,requests,tags,broadcast,…}`, `/models[/{id}]` (публичный каталог).

## Email ([EmailDispatchWorker.cs](Email/EmailDispatchWorker.cs))

Асинхронная отправка через `Channel`-очередь. Воркер дренит её последовательно: на сообщение до **3 попыток** с backoff 1с/2с/4с, на финальном провале сообщение **отбрасывается** (log error). После каждого — пауза `EmailSettings.BatchDelayMs` (дефолт 1000мс ≈ 1 msg/s, throttle под лимиты SMTP). **Без настроенного SMTP-секрета коды верификации/инвайты не уходят** (`docs/email-integration-plan.md`).

## Трекинг визитов ([SiteVisitTrackingMiddleware.cs](Services/SiteStats/SiteVisitTrackingMiddleware.cs))

Считает уникальных посетителей сайта, **включая незарегистрированных**, и источник перехода —
это данные для секции «Посетители сайта» дашборда Grafana `ORR Users`.

- Строка пишется **после** ответа и только если он оказался HTML (`text/html`, статус < 400) —
  так отсеиваются статика, редиректы, API и вебхуки без списка расширений. Плюс явный
  чёрный список префиксов (`/health`, `/metrics`, `/internal`, `/webhook`, `/.well-known`).
- Посетитель = `HMAC-SHA256(ProductAnalytics:IdentitySalt, ip + "|" + user-agent)`
  (`VisitorIdHasher`). **Сырой IP не сохраняется, кук не ставим** — согласие не требуется.
  Соль стабильная: без неё нельзя отличить вернувшегося посетителя от нового.
  Ограничения: один NAT = один посетитель; `X-Forwarded-For` подделывается клиентом.
- У залогиненного визита заполняется `distinct_id` — тот же `UserPseudonymizer`, что и в
  `analytics.events`, поэтому воронка «визит → регистрация» считается одним SQL.
- Очередь + фоновой флаш (`SiteVisitQueue` → `SiteVisitFlushWorker` в BuildingBlocks):
  очередь переполнена — визит отбрасывается, ошибка вставки — батч дропается с логом.
  Рендер страницы не блокируется никогда.
- Выключается `SiteVisits__Enabled=false`; без настроенного `ProductAnalytics`-синка
  middleware вообще не регистрируется (`Program.cs` проверяет наличие `SiteVisitQueue` в DI).
- Схема — `db/migrations/analytics/002_site_visits.sql`, применяет **BillingService**
  (он владеет базой `orr_analytics`).

## Подводные камни

- **Только gRPC/HTTP-клиенты** — доменной БД у WebApp нет, вся логика в backend-сервисах.
  Единственное прямое подключение к Postgres — запись визитов в `orr_analytics` (см. выше);
  для доменных данных так делать нельзя.
- **`RouterNodeApiProvider`** ходит на ноды `sk-or-*` ключом. Маппинг `RouterNodeTokens:{controlToken}` → `sk-or-*` задаётся в env (см. корневой CLAUDE.md, шаг 5 регистрации ноды). **Менять только через kustomize+git** — ArgoCD (`selfHeal`) откатит `kubectl set env`.
- `/workspace/settings` — пустая заглушка.
- Google-логин не появится, пока не заданы оба `Google:*` секрета.

## E2E

Playwright (`tests/OpenRussiaRouter.E2ETests`). Креды dev: `admin@example.com` / `StrongPassword123!`, UI `http://localhost`. Конфиг `e2e.local.json` (gitignored).
