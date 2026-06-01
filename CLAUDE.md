# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```powershell
# Restore dependencies
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1

# Build solution
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1

# Run all tests
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1

# Run a single test project
dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1

# Run individual services
dotnet run --project src/OpenRussiaRouter.ApiGateway
dotnet run --project src/OpenRussiaRouter.AuthService
dotnet run --project src/OpenRussiaRouter.BillingService
dotnet run --project src/OpenRussiaRouter.ControlPlane
dotnet run --project src/OpenRussiaRouter.WebApp
```

**PostgreSQL integration tests** (Testcontainers, skip gracefully without Docker):
- `ORR_AUTH_POSTGRES_TEST_CONNECTION` — override для AuthService (обычно не нужен, Testcontainers сам поднимает контейнер)
- `ORR_BILLING_POSTGRES_TEST_CONNECTION`
- `ORR_CONTROL_POSTGRES_TEST_CONNECTION`

**E2E tests** (Playwright, opt-in):
```powershell
# До первого запуска установить браузеры:
pwsh tests/OpenRussiaRouter.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
# Конфиг: tests/OpenRussiaRouter.E2ETests/e2e.local.json (gitignored)
```

## Architecture Overview

OpenRussiaRouter is an OpenAI-compatible LLM gateway with $ billing, built as .NET 8 microservices communicating via gRPC.

### Services

| Service | Role | Default Port |
|---|---|---|
| **ApiGateway** | Public HTTP entry point, OpenAI-compatible API | HTTP 5000 |
| **AuthService** | User accounts, sessions, API key lifecycle | gRPC 5001 |
| **BillingService** | Balance ledger, model pricing, usage tracking | gRPC 5002 |
| **ControlPlane** | 9Router node registry, health checks, leasing | gRPC 5003 |
| **WebApp** | ASP.NET Core Razor Pages user cabinet & admin dashboard | HTTP 5004 |
| **CollectorService** | Периодический опрос нод/OpenRouter, in-memory кеш, отдаёт снапшоты по gRPC | gRPC 5005 |
| **BuildingBlocks** | Shared library: Money, TokenUsage, errors, migrations | — |
| **Contracts** | Shared DTOs + `.proto` files for all gRPC interfaces | — |

### API Gateway Request Flow

`POST /v1/chat/completions` (non-streaming и streaming):
1. Extract Bearer token → validate via AuthService gRPC
2. Check balance + model availability via BillingService gRPC
3. Select router node via ControlPlane gRPC (least-load)
4. Patch request JSON: replace `model` with `router_model_id` from billing catalog
5. Forward to 9Router node (non-streaming via YARP, streaming via `HttpCompletionOption.ResponseHeadersRead`)
6. **Retry** on 5xx / network error before first byte reaches client (one attempt, different node)
7. Extract token usage from response (JSON field or final SSE chunk with `include_usage`)
8. Record usage event asynchronously (fire-and-forget gRPC call to BillingService)
9. Release lease in ControlPlane

Endpoints:
- `POST /v1/chat/completions` — non-streaming и streaming (автодетект по полю `stream`)
- `POST /v1/chat/completions/stream` — явный streaming alias
- `GET /v1/models` — список enabled моделей из BillingService
- `GET /health`, `GET /ready`

### Key Patterns

- **Repository pattern**: Each service has `I{Service}Repository` with both Dapper/PostgreSQL and in-memory implementations (in-memory used in tests)
- **Extension method registration**: `Add{Service}Infrastructure()` / `Add{Service}Application()` in `Infrastructure/Startup/`
- **SQL migrations**: Raw `.sql` files in `db/migrations/{service}/`, auto-applied on startup via `ApplySqlMigrationsAsync()`
- **gRPC contracts**: `.proto` files in `src/OpenRussiaRouter.Contracts/Protos/` — `auth.proto`, `billing.proto`, `control-plane.proto`
- **No Entity Framework** — Dapper + Npgsql only
- **Correlation IDs**: `X-Correlation-Id` header propagated via `CorrelationIdReader`, injected into Serilog context
- **WebApp** — Razor Pages (`.cshtml` + `.cshtml.cs`), cookie auth, gRPC clients in `Services/`

### Shared BuildingBlocks

- `MoneyAmount` — record struct with currency, arithmetic operators
- `TokenUsage` + `UsageCostCalculator` — token billing math
- `TokenUsageExtractor` — extracts usage from JSON/SSE (non-streaming and streaming paths)
- `ApiError` + `ErrorCodes` — standardized error responses
- `PostgresSqlMigrationRunner` — runs SQL migration scripts from embedded resources
- Serilog extensions for Seq integration

### Configuration

gRPC service URLs and DB connection strings live in `appsettings.json` per service:

```json
{
  "GrpcServices": {
    "Auth": "http://localhost:5001",
    "Billing": "http://localhost:5002",
    "ControlPlane": "http://localhost:5003"
  }
}
```

### Database Migrations

SQL scripts are in `db/migrations/{auth|billing|control}/`. They run automatically on startup — add new files with the next numeric prefix (e.g., `004_*.sql`).

Current migration counts: auth (4), billing (5), control (3).

### WebApp Pages

```
/login, /logout, /invite/{token}
/                          — Workspace Index (balance overview)
/workspace/api-keys        — Create / rename / revoke API keys
/workspace/activity        — Usage records table
/workspace/credits         — Balance + top-up history
/workspace/settings        — stub (empty)
/admin                     — Admin Index
/admin/users               — List users, create invite, virtual top-up
/admin/models              — CRUD model/pricing catalog
/admin/nodes               — List nodes, add, disable/enable
/admin/nodes/{id}          — Node details + integration keys
/admin/nodes/{id}/limits   — Provider quota dashboard
/admin/anomalies           — Billing anomalies
/models, /models/{id}      — Public model catalog (user-facing)
```

### 9Router Integration & Two Types of Tokens

Интеграция с 9Router использует **два разных типа токенов** — не путать:

#### 1. Токен ноды (Control Token)

Задаётся в настройках ноды при регистрации (поле **Control Token** в Admin → Nodes).

- Это `ROUTER_CONTROL_TOKEN` из конфига 9Router
- Используется ControlPlane для аутентификации к admin API 9Router (`/api/integrations/*`)
- Позволяет пушить/отзывать пользовательские ключи на ноду
- Хранится в `control.router_nodes.control_token` как **фактическое значение токена** (не имя секрета)
- `RouterNodeProvider` отправляет его напрямую как `Authorization: Bearer <control_token>` — никакого разрешения через config нет
- В k8s задаётся через `f9router-secret` (поле `ROUTER_CONTROL_TOKEN`), значение вводится вручную при регистрации ноды
- **Не хранится в репозитории** — только в k8s Secret и в БД ControlPlane

> **Важно**: В CLAUDE.md ранее было описано, что control_token хранит «имя секрета» и резолвится через `RouterHealthChecks:NodeTokens`. Это **неверно** — в коде `RouterNodeProvider.cs` нет такой логики. Поле `control_token` хранит сам токен.

#### 2. Пользовательские API-ключи

Создаются пользователями в WebApp (Workspace → API Keys).

- Формат: `sk-or-<random>` (требование 9Router)
- Хранятся в AuthService (хеш в `auth.api_keys`)
- При создании автоматически пушатся на все активные ноды через ControlPlane → `POST /api/integrations/keys`
- При создании пушится на все активные ноды через ControlPlane → `POST /api/integrations/keys`; при отзыве деактивируется через `PATCH /api/integrations/keys/{id}/status`. Маппинг `(api_key_id, node_id, router_key_id)` хранится в `control.integration_key_sync`.
- Используются для аутентификации при вызове LLM-моделей через 9Router proxy
- Видны в дашборде 9Router (`/dashboard/endpoint`)

### Регистрация новой ноды 9Router в Kubernetes

Полная пошаговая процедура. Применима при первоначальной настройке кластера или добавлении новой ноды.

#### Предварительные условия

- Кластер `kind` поднят, ArgoCD синхронизирован.
- f9router pod запущен (`kubectl get pods -n orr-dev -l app=f9router`).
- Есть доступ к `http://localhost/admin/nodes` (admin@example.com / StrongPassword123!).

#### 1. Создать / обновить f9router-secret

```powershell
# Сгенерировать токен (40 символов)
$ct = -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 40 | % { [char]$_ })

kubectl create secret generic f9router-secret -n orr-dev `
  --from-literal=ROUTER_CONTROL_TOKEN="$ct" `
  --from-literal=OPENROUTER_API_KEY="sk-or-v2-..." `
  --dry-run=client -o yaml | kubectl apply -f -

# Перезапустить pod, чтобы подхватил новый секрет
kubectl rollout restart deployment/f9router -n orr-dev
kubectl rollout status deployment/f9router -n orr-dev
```

#### 2. Определить endpoint URL

Для постоянной регистрации — k8s Service DNS:
```
http://f9router/
```
Service `f9router` слушает `:80` → проксирует на `containerPort 20128`.

Для разовой отладки — pod IP:
```powershell
kubectl get pod -n orr-dev -l app=f9router -o jsonpath='{.items[0].status.podIP}'
# → http://<pod-ip>:20128/
```

#### 3. Зарегистрировать ноду в Admin UI

`http://localhost/admin/nodes` → **Add Node**:
- **Endpoint URL**: `http://f9router/`
- **Control Token**: значение `$ct` из шага 1 (сам токен, не алиас)

После добавления нода должна стать `Healthy` за 30–60 сек — ControlPlane проверяет `GET /api/health` (без авторизации, HTTP 200).

#### 4. Убедиться, что HealthPath = `/api/health`

Базовый дефолт в `infra/kustomize/base/services/control-plane.yaml` — `/v1/models` (требует auth, 401).
Dev overlay патч `infra/kustomize/overlays/dev/services/control-plane-env-patch.yaml` переопределяет на `/api/health`.
Если нода не становится Healthy — проверить:

```powershell
kubectl get deployment -n orr-dev control-plane `
  -o jsonpath='{.spec.template.spec.containers[0].env}' `
  | ConvertFrom-Json | Where-Object name -eq RouterHealthChecks__HealthPath
# Должно быть: /api/health
```

#### 5. Настроить маппинг токенов для WebApp

WebApp запрашивает `/v1/models` у ноды, используя `sk-or-*` ключ (f9router требует его для запросов с IP, отличных от 127.0.0.1).

Маппинг: `RouterNodeTokens:{controlToken}` → `sk-or-*` API ключ.

Добавить в `infra/kustomize/overlays/dev/services/webapp-env-patch.yaml`:

```yaml
env:
  - name: RouterNodeTokens__<ЗНАЧЕНИЕ_CONTROL_TOKEN>
    value: "sk-or-<активный-api-ключ>"
```

Пример:
```yaml
- name: RouterNodeTokens__GaChSrSJDnxKAYTDaAMZ0UyemR1cQSUtiF2PUnXSM
  value: "sk-or-SEAiRlwrPc0KVJoOCwYB1DmMfn_lQfDfj7qWZ5vHcaCg4EQC"
```

**Обязательно закоммитить и запушить** — ArgoCD (`selfHeal: true`) откатит `kubectl set env`.

#### 6. Пушнуть API ключ на ноду вручную (если нужно)

Ключ автоматически пушится на ноды при создании в WebApp. Если нода добавлена позже — пуш вручную:

```powershell
# Из pod control-plane (имеет доступ в кластер)
kubectl exec -n orr-dev deploy/control-plane -- wget -qO- `
  --header="Authorization: Bearer <CONTROL_TOKEN>" `
  --header="Content-Type: application/json" `
  --post-data='{"key":"sk-or-<key>","name":"admin-seed"}' `
  http://f9router/api/integrations/keys
# Ожидаемый ответ: HTTP 201 + JSON с router_key_id
```

#### Ключевые заблуждения (не повторять)

| Неверно | Верно |
|---|---|
| `control_token` в БД = имя секрета | `control_token` = реальное значение токена |
| `RouterHealthChecks:NodeTokens:{name}` резолвит токен | Такой логики в коде нет — `RouterNodeProvider` использует `node.ControlToken` напрямую |
| `kubectl set env` — безопасный способ настройки | ArgoCD откатит через ~1–2 мин; менять только через kustomize + git |
| HealthPath `/v1/models` работает | 401 от f9router; нужно `/api/health` |

## Current Status (MVP completeness)

### Готово

| Area | What's done |
|---|---|
| Platform | Solution, all projects build, shared contracts, migrations runner |
| AuthService | Full domain + PostgreSQL persistence, all gRPC methods, unit + integration + PG tests |
| BillingService | Ledger, model catalog, usage recording, all gRPC methods, unit + integration + PG tests |
| ControlPlane | Node registry, health checks, lease management, 9Router admin API client, all gRPC methods |
| ApiGateway | Non-streaming + SSE streaming, retry/failover, YARP proxy, OpenAI error shape |
| WebApp | All user and admin Razor Pages, cookie auth, gRPC clients |
| E2E Tests | Playwright: API key lifecycle, node lifecycle |
| Infra | Docker Compose, Kubernetes/Kustomize/ArgoCD |

### Не готово / заглушки

| Area | Gap |
|---|---|
| WebApp `/workspace/settings` | Страница — пустая заглушка |
| ~~API key revoke → nodes~~ | Реализовано: маппинг в `control.integration_key_sync`, revoke деактивирует на нодах |
| gRPC error interceptors | Нет structured gRPC error details (PLATFORM-002) |
| Metrics / tracing | Нет OpenTelemetry, нет метрик request count/latency (PLATFORM-003) |
| Log redaction | Нет явного хелпера для redact sensitive fields в логах |
| Invite delivery | Invite token виден только в admin create response, нет email и явного UI-отображения |
| Immutable price history | В `billing.model_prices` один active row на модель, не versioned history (BILLING-003) |
| Billing outbox | Usage events — fire-and-forget in-process gRPC, без PostgreSQL-backed outbox (BILLING-004) |
| Health check cooldown | Нет cooldown/flapping policy в `RouterHealthCheckWorker` (CONTROL-002) |
| ~~Capability-aware routing~~ | Реализовано: `RouterControlPlaneService` фильтрует кандидатов по `NodeModelCapabilityStore` (наполняется `CapabilityReplicaRefresher` из CollectorService), при пустом store — fallback на least-load без фильтра (CONTROL-003) |
| Lease recovery | После рестарта ControlPlane in-flight счётчики сбрасываются, нет recovery (CONTROL-004) |
