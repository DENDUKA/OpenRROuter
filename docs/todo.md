# OpenRussiaRouter — TODO / Статус задач

> Обновлено: 2026-06-14 (исходный MVP-трекер от 2026-05-15 + дельта после MVP)
>
> ⚠️ **Это исходный трекер фазы MVP.** Актуальный статус готовности к проду — в
> `docs/production-readiness-review.md`. Историю строк ниже не переписываем (они отражают MVP);
> две поправки терминологии: UI — **Razor Pages**, а не Blazor; пути кабинета — **`/workspace/*`**,
> а не `/cabinet/*` (см. CLAUDE.md → WebApp Pages). Что изменилось после MVP — в разделе
> «Дельта после MVP» в конце файла.

---

## PLATFORM ✅

| Задача | Статус | Описание |
|---|---|---|
| PLATFORM-001 | ✅ Готово | Структура решения, BuildingBlocks, Contracts, gRPC-контракты |
| PLATFORM-002 | ✅ Готово | Модель gRPC-ошибок (ErrorCodes, ApiError) |
| PLATFORM-003 | ✅ Готово | Serilog + Seq, CorrelationId middleware |

---

## AUTH SERVICE ✅

| Задача | Статус | Описание |
|---|---|---|
| AUTH-001 | ✅ Готово | UserAccount, Invite, UserRole; invite с хешом токена, one-time механизм |
| AUTH-002 | ✅ Готово | Login/сессия с ролями Admin/User |
| AUTH-003 | ✅ Готово | ApiKey lifecycle (create/rename/revoke, prefix, secret hash) |
| AUTH-004 | ✅ Готово | gRPC AuthService (ValidateApiKey, Login, CreateInvite, CompleteInvite, ...) |
| KEYSYNC-001 | ✅ Готово | gRPC метод `ListActiveApiKeyHashes` — возвращает активные prefix+hash пары |

---

## BILLING SERVICE ✅

| Задача | Статус | Описание |
|---|---|---|
| BILLING-001 | ✅ Готово | Баланс + append-only Ledger с idempotency key |
| BILLING-002 | ✅ Готово | Pre-request проверка баланса (balance > 0) |
| BILLING-003 | ✅ Готово | Каталог моделей с прайсингом (input/output/cache per million) |
| BILLING-004 | ✅ Готово | UsageCostCalculator, запись UsageRecord (без тел промптов) |
| BILLING-005 | ✅ Готово | BillingAnomaly (невалидные токены, отсутствующие цены) |
| BILLING-006 | ❌ Не сделано | UsageAggregates — агрегация по дням/моделям/пользователям, gRPC `GetUsageAggregates` |
| BILLING-007 | ✅ Готово | gRPC BillingService (CheckBalanceAndModel, RecordUsage, ListPublicModels, ...) |

---

## CONTROL PLANE ✅

| Задача | Статус | Описание |
|---|---|---|
| CONTROL-001 | ✅ Готово | RouterNode registry (endpoint, token hash, weight, enabled) |
| CONTROL-002 | ✅ Готово | Health check background worker |
| CONTROL-003 | ✅ Готово | Least-load selection среди healthy-узлов |
| CONTROL-004 | ✅ Готово | RouterLease (in-flight counter, TTL, cleanup worker) |
| CONTROL-005 | ✅ Готово | gRPC ControlPlaneService (SelectRouterNode, ReleaseRouterNode, ...) |
| KEYSYNC-002 | ✅ Готово | `RouterKeySyncService` — синхронизация ключей на 9Router ноды через `/internal/keys/sync` |
| KEYSYNC-003 | ✅ Готово | Fire-and-forget `SyncToNodeAsync` при регистрации ноды |
| KEYSYNC-004 | ✅ Готово | `KeySyncWorker` — периодическая синхронизация каждые 5 минут |

---

## API GATEWAY ✅

| Задача | Статус | Описание |
|---|---|---|
| GATEWAY-001 | ✅ Готово | `GET /v1/models` с Bearer-auth |
| GATEWAY-002 | ✅ Готово | `POST /v1/chat/completions` non-streaming (auth → billing → control → proxy → usage) |
| GATEWAY-003 | ✅ Готово | SSE-стриминг: `RouterStreamSession.CopyToAsync`, `SseRequestPatcher`, usage из финального чанка |
| GATEWAY-004 | ✅ Готово | Retry/failover на другую ноду до первого SSE-чанка |
| GATEWAY-005 | ✅ Готово | Middleware валидации API ключа |
| GATEWAY-006 | ✅ Готово | Маппинг публичных ошибок (без внутренних деталей) |
| KEYSYNC-005 | ✅ Готово | Gateway пробрасывает оригинальный `Authorization: Bearer {raw_key}` на 9Router |

---

## BLAZOR UI ✅

| Задача | Статус | Описание |
|---|---|---|
| UI-001 | ✅ Готово | Login / Invite completion страницы |
| UI-002 | ✅ Готово | `/cabinet/keys` — CRUD API ключей |
| UI-003 | ✅ Готово | `/cabinet/usage` — история использования |
| UI-004 | ✅ Готово | `/admin/users` — управление пользователями, инвайты, пополнение баланса |
| UI-005 | ✅ Готово | `/admin/models` — управление моделями и ценами |
| UI-006 | ✅ Готово | `/admin/nodes` — управление роутер-нодами |
| UI-007 | ✅ Готово | `/admin/anomalies` — список биллинговых аномалий |
| UI-DESIGN | ✅ Готово | Dark minimal SaaS дизайн: CSS custom properties, sidebar, DataTable компонент |
| UI-DESIGN-005 | ❌ Не сделано | Страница `/cabinet/balance` — баланс пользователя, история пополнений |

---

## ИНФРАСТРУКТУРА

| Задача | Статус | Описание |
|---|---|---|
| INFRA-001 | ✅ Готово | `docker-compose.yml` + `.env.example` для локальной разработки |
| INFRA-002 | ✅ Готово | 14 K8s NetworkPolicies (default-deny + granular allow); `app: db-create` label на migration Job |
| INFRA-003 | ❌ Не сделано | SOPS + Age: зашифровать `infra/k8s/secrets/*.yaml`; убрать plain-text `postgres-secret.yaml` |
| INFRA-004 | ✅ Готово | GitHub Actions CI (`ci.yml`/`build-deploy.yml`): build + test + docker push; CD (`cd.yml`): kustomize image pin |
| INFRA-005 | ✅ Готово | `appsettings.Production.json` для всех 5 сервисов |
| K8s-FIX | ✅ Готово | Исправлены env var имена (`GrpcServices__Auth/Billing/ControlPlane`) в `api-gateway.yaml`; добавлен `GrpcServices__Auth` в `control-plane.yaml` |
| K8s-MIGRATIONS | ✅ Готово | Migration Job с ArgoCD PostSync hook; postgres YAML использует `POSTGRES_DB: postgres` |

---

## 9ROUTER (внешний компонент — TypeScript форк на VPS)

> Реализуется отдельно на стороне `ai.web-point.top`. Подробное ТЗ: `docs/9router-todo.md`

| Задача | Статус | Описание |
|---|---|---|
| ROUTER-001 | ❌ Не сделано | `GET /internal/health` с `Authorization: Bearer {NODE_TOKEN}` |
| ROUTER-002 | ❌ Не сделано | Стандартизированный `usage` payload (prompt_tokens/completion_tokens/cached_tokens) в streaming и non-streaming |
| ROUTER-003 | ❌ Не сделано | Стандартизированная форма ошибок с полем `retryable` |
| ROUTER-004 | ❌ Не сделано | Валидация `X-ORR-Node-Token` на всех `/internal/*` и `/v1/*` |
| ROUTER-005 | ❌ Не сделано | Маппинг моделей (`openai/gpt-4o` → OpenAI `gpt-4o`, etc.) |
| ROUTER-006 | ❌ Не сделано | `POST /internal/keys/sync`, `DELETE /internal/keys/{prefix}`, Bearer middleware для `/v1/*` |
| ROUTER-007 | ❌ Не сделано | Заменить собственную auth-систему 9Router на проверку через синхронизированное хранилище |

---

## ТЕСТЫ

| Задача | Статус | Описание |
|---|---|---|
| TEST-001 | ✅ Готово | ApiGateway integration: auth/billing/control/router happy+error paths (10 тестов) |
| TEST-002 | ✅ Готово | Streaming тесты: SSE chunks, usage extraction, retry до первого чанка (13 тестов) |
| TEST-003 | ✅ Частично | E2E-002 (zero balance → 402), E2E-003 (failover), E2E-004 (нет промптов в UsageEvent) — как integration тесты ApiGateway |
| TEST-003 | ❌ Не сделано | E2E-001: полный flow invite→login→key→topup→chat→usage в реальном стеке (требует Testcontainers для всех сервисов) |
| TEST-004 | ✅ Готово | PostgreSQL тесты: `ListActiveApiKeyHashes` (фильтрация отозванных, сортировка, пустая таблица) |
| TEST-004+ | ❌ Не сделано | RouterKeySyncService тесты (mock HTTP + mock gRPC AuthService) |

---

## BILLING-006 — UsageAggregates (детали)

```
Новый gRPC метод: GetUsageAggregates(userId, from, to, groupBy)
SQL агрегация: расход по дням / по моделям / по пользователям
Применить в WebApp: графики на /cabinet/usage и /admin/users
```

Файлы:
- `src/OpenRussiaRouter.BillingService/` (новый репозиторий + gRPC метод)
- `src/OpenRussiaRouter.Contracts/Protos/billing.proto`
- `src/OpenRussiaRouter.WebApp/Pages/Cabinet/Usage.razor`

---

## Порядок оставшихся задач

```
ROUTER-001..007  (форк 9Router — независимо, на VPS)
      ↓
BILLING-006      (UsageAggregates + /cabinet/balance UI)
      ↓
INFRA-003        (SOPS + Age шифрование secrets)
      ↓
TEST-003 E2E-001 (полный stack E2E с Testcontainers)
TEST-004+        (RouterKeySyncService unit tests)
```

---

## Дельта после MVP (2026-06)

Что появилось/закрылось поверх MVP-трекера выше. Полный контекст и приоритеты — в
`docs/production-readiness-review.md`.

### Сделано после MVP

| Область | Статус | Что |
|---|---|---|
| Email + signup | ✅ Готово | Brevo SMTP (`IEmailSender`/worker), invite-email + broadcast; self-serve signup → 6-значный код → `/verify-email`. См. `docs/email-integration-plan.md`. Требует SMTP-секрета на проде |
| Password reset / lockout | ✅ Готово | `/forgot-password` → код → `/reset-password`; смена пароля в `/workspace/settings`; lockout 5 попыток / 15 мин. Миграция auth 011 |
| Метрики / трейсинг | ✅ Готово | OpenTelemetry-метрики (был PLATFORM-003-пробел) |
| Платёжный каркас | ✅ Готово | `IPaymentProvider`, `PaymentCheckoutService`, webhook + verify + per-IP rate limit, reconciliation/recovery воркеры, FX (ЦБ РФ), валюта отображения. См. `docs/billing-payments-implementation-plan.md` |
| Реальная касса (код) | ✅ Готово | `RollypayPaymentProvider` (X-API-Key + nonce, HMAC-вебхук). Включение — не-кодовое (касса/секреты/webhook-URL) |
| Gateway rate limiting | ✅ Готово | Per-API-key fixed-window на `/v1/chat/completions[/stream]`, `429` в OpenAI-формате |
| Gateway retry cap | ✅ Готово | `MaxRouterAttempts=5` — закрыт баг бесконечного ретрая на 5xx |
| Usage delivery resilience | 🟡 Частично | Polly retry + circuit breaker на gRPC-канал биллинга (BILLING-004). **Durable outbox всё ещё нет** — при падении Gateway/долгом ауте списание теряется |
| Lease leak | ✅ Готово | Release lease при обрыве клиента (`CancellationToken.None` во всех ветках `ChatCompletionHandler`) |
| Локализация | ✅ Готово | EN/RU `.resx` + переключатель языка (`SetLanguage`/кука) + переключатель валюты ₽/$/€ |
| Юр-страницы | 🟡 Код готов | Оферта + политика (152-ФЗ), `LegalDocumentService`, согласие гейтит signup/invite/Google/checkout. Не-кодовое — заполнить реквизиты оператора + домен |

### Остаётся открытым (приоритет — в ревью)

| Задача | Статус | Что |
|---|---|---|
| HTTPS / TLS | ❌ Блокер | Оба деплоя — голый HTTP. Главный гейт: разблокирует платежи (webhook-URL) и публикацию оферты |
| BILLING-004 outbox | ❌ Не сделано | Durable PostgreSQL-backed outbox для usage (retry/CB — лишь смягчение) |
| BILLING-003 price history | ❌ Не сделано | Immutable versioned прайс вместо одного active row |
| BILLING-006 | ❌ Не сделано | UsageAggregates + графики расходов на Credits/Activity |
| CONTROL-004 lease recovery | ❌ Не сделано | После рестарта ControlPlane in-flight счётчики сбрасываются (leak на дисконнект — закрыт) |
| INFRA-003 | ❌ Не сделано | SOPS+Age: plain-text секреты в `infra/kustomize/base/` |
| Onboarding / playground | ❌ Не сделано | Чеклист «3 шага» на `/` + пользовательский playground |
| Low-balance warning | ❌ Не сделано | Баннер + письмо при пересечении порога |
| Описания моделей (task D) | ❌ Не сделано | Санитайзер markdown из OpenRouter (`docs/tasks/orr-userfriendly-plan.md`) |
