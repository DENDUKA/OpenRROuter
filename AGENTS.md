# AGENTS.md

> **IMPORTANT**: ALWAYS respond to the user in the Russian language (Всегда отвечай на русском языке).

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

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
dotnet run --project src/OpenRussiaRouter.CollectorService
dotnet run --project src/OpenRussiaRouter.WebApp
```

**Why `-nr:false -m:1`**: Some sandboxed Windows environments fail MSBuild node reuse/parallel restore without a useful error message.

**Build warnings are errors**: `TreatWarningsAsErrors` is `true` in `Directory.Build.props`. A warning = build failure.

**PostgreSQL integration tests** are opt-in via environment variables (Testcontainers, skip gracefully without Docker):
- `ORR_AUTH_POSTGRES_TEST_CONNECTION`
- `ORR_BILLING_POSTGRES_TEST_CONNECTION`
- `ORR_CONTROL_POSTGRES_TEST_CONNECTION`

**E2E tests** (Playwright, opt-in):
```powershell
# First time only — install browsers:
pwsh tests/OpenRussiaRouter.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
# Config: tests/OpenRussiaRouter.E2ETests/e2e.local.json (gitignored)
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
| **CollectorService** | Periodic node/OpenRouter polling, in-memory cache, gRPC snapshots | gRPC 5005 |
| **BuildingBlocks** | Shared library: Money, TokenUsage, errors, migrations | — |
| **Contracts** | Shared DTOs + `.proto` files for all gRPC interfaces | — |

### API Gateway Request Flow

`POST /v1/chat/completions` (non-streaming and streaming):
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
- `POST /v1/chat/completions` — non-streaming and streaming (auto-detected by `stream` field)
- `POST /v1/chat/completions/stream` — explicit streaming alias
- `GET /v1/models` — enabled models from BillingService
- `GET /health`, `GET /ready`

### Key Patterns

- **Repository pattern**: Each service has `I{Service}Repository` with both Dapper/PostgreSQL and in-memory implementations (in-memory used in tests)
- **Extension method registration**: `Add{Service}Infrastructure()` / `Add{Service}Application()` in `Infrastructure/Startup/`
- **SQL migrations**: Raw `.sql` files in `db/migrations/{service}/`, auto-applied on startup via `ApplySqlMigrationsAsync()`
- **gRPC contracts**: `.proto` files in `src/OpenRussiaRouter.Contracts/Protos/` — `auth.proto`, `billing.proto`, `control-plane.proto`
- **No Entity Framework** — Dapper + Npgsql only
- **Central Package Management** — all NuGet versions in `Directory.Packages.props`
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

SQL scripts are in `db/migrations/{auth|billing|control}/`. They run automatically on startup — add new files with the next numeric prefix (e.g., `016_*.sql`).

Current migration counts: auth (10), billing (15), control (4).

### 9Router Integration — Two Types of Tokens

| Token | What it is | Where used |
|---|---|---|
| **Control Token** (`ROUTER_CONTROL_TOKEN`) | Actual token value (not a secret name) | ControlPlane → f9router admin API (`Authorization: Bearer <token>`) |
| **User API Key** (`sk-or-*`) | Created by users in WebApp | Authenticated calls to LLM models via 9Router proxy |

**Common mistake**: `control_token` in the DB is the real token value, NOT a secret name. `RouterNodeProvider` sends it directly — there is no secret-resolution step.

## Deployment Memory

Dev Kubernetes image delivery is owned by Argo CD Image Updater, not GitHub Actions.

- GitHub Actions restores, builds, tests, and pushes GHCR images with `sha-<commit>` and `dev-latest` tags.
- Argo CD Image Updater watches `dev-latest`, resolves digests, writes image overrides, and lets Argo CD sync `orr-services`.
- **Do not** reintroduce a GitHub Actions job that commits image tags into Kustomize manifests.
- **Do not** use `kubectl set env` — ArgoCD (`selfHeal: true`) reverts it within ~1–2 min. Change only through kustomize + git.

Full 9Router node registration procedure: `docs/argo-k8s-dev-runbook.md` and CLAUDE.md.
