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

**PostgreSQL integration tests** are opt-in via environment variables:
- `ORR_AUTH_POSTGRES_TEST_CONNECTION`
- `ORR_BILLING_POSTGRES_TEST_CONNECTION`
- `ORR_CONTROL_POSTGRES_TEST_CONNECTION`

## Architecture Overview

OpenRussiaRouter is an OpenAI-compatible LLM gateway with RUB billing, built as .NET 8 microservices communicating via gRPC.

### Services

| Service | Role | Default Port |
|---|---|---|
| **ApiGateway** | Public HTTP entry point, OpenAI-compatible API | HTTP |
| **AuthService** | User accounts, sessions, API key lifecycle | gRPC 5001 |
| **BillingService** | Balance ledger, model pricing, usage tracking | gRPC 5002 |
| **ControlPlane** | 9Router node registry, health checks, leasing | gRPC 5003 |
| **WebApp** | Blazor user cabinet & admin dashboard | 5004 |
| **BuildingBlocks** | Shared library: Money, TokenUsage, errors, migrations | — |
| **Contracts** | Shared DTOs + `.proto` files for all gRPC interfaces | — |

### API Gateway Request Flow

`POST /v1/chat/completions`:
1. Extract Bearer token → validate via AuthService gRPC
2. Check balance + model availability via BillingService gRPC
3. Select router node via ControlPlane gRPC
4. Forward request to 9Router node via YARP reverse proxy
5. Extract token usage from response
6. Record usage event asynchronously (fire-and-forget)

### Key Patterns

- **Repository pattern**: Each service has `I{Service}Repository` with both Dapper/PostgreSQL and in-memory implementations (in-memory used in tests)
- **Extension method registration**: `Add{Service}Infrastructure()` / `Add{Service}Application()` in `Infrastructure/Startup/`
- **SQL migrations**: Raw `.sql` files in `db/migrations/{service}/`, auto-applied on startup via `ApplySqlMigrationsAsync()`
- **gRPC contracts**: `.proto` files in `src/OpenRussiaRouter.Contracts/Protos/` — `auth.proto`, `billing.proto`, `control-plane.proto`
- **No Entity Framework** — Dapper + Npgsql only
- **Correlation IDs**: `X-Correlation-Id` header propagated via `CorrelationIdReader`, injected into Serilog context

### Shared BuildingBlocks

- `MoneyAmount` — record struct with currency, arithmetic operators
- `TokenUsage` + `UsageCostCalculator` — token billing math
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

### 9Router Integration & Two Types of Tokens

Интеграция с 9Router использует **два разных типа токенов** — не путать:

#### 1. Токен ноды (Control Token)

Задаётся в настройках ноды при регистрации (поле **Control Token Secret** в Admin → Nodes).

- Это `ROUTER_CONTROL_TOKEN` из конфига 9Router
- Используется ControlPlane для аутентификации к admin API 9Router (`/api/integrations/*`)
- Позволяет пушить/отзывать пользовательские ключи на ноду
- Хранится в `control.router_nodes.token_secret_name` как **имя секрета**
- Реальное значение резолвится из конфига ControlPlane: `RouterHealthChecks:NodeTokens:{tokenSecretName}`
- В Kubernetes задаётся через Secret → env var `RouterHealthChecks__NodeTokens__{tokenSecretName}`

#### 2. Пользовательские API-ключи

Создаются пользователями в WebApp (Workspace → API Keys).

- Формат: `sk-or-<random>` (требование 9Router)
- Хранятся в AuthService (хеш в `auth.api_keys`)
- При создании автоматически пушатся на все активные ноды через ControlPlane → `POST /api/integrations/keys`
- Используются для аутентификации при вызове LLM-моделей через 9Router proxy
- Видны в дашборде 9Router (`/dashboard/endpoint`)
