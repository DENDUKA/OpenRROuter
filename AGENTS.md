# AGENTS.md

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
dotnet run --project src/OpenRussiaRouter.WebApp
```

**PostgreSQL integration tests** are opt-in via environment variables:
- `ORR_AUTH_POSTGRES_TEST_CONNECTION`
- `ORR_BILLING_POSTGRES_TEST_CONNECTION`
- `ORR_CONTROL_POSTGRES_TEST_CONNECTION`

## Architecture Overview

OpenRussiaRouter is an OpenAI-compatible LLM gateway with $ billing, built as .NET 8 microservices communicating via gRPC.

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
2. Check balance + model availability via BillingService gRPC. The BillingService resolves the user-facing `ModelId` (e.g. `ORR/Gemini-3.1-Pro`) to the internal `RouterModelId` (e.g. `ag/gemini-pro-agent`).
3. Select router node via ControlPlane gRPC using the resolved `RouterModelId`.
4. Patch the request JSON:
   - Replace the `model` field in the payload with the internal `RouterModelId`.
   - If either the user-facing `ModelId` or the internal `RouterModelId` contains the word "gemini" (case-insensitive), apply `GeminiSchemaPatcher` to sanitize schemas (e.g. `tools`/`functions` formatting) to comply with Google's strict requirements.
5. Forward request to 9Router node via YARP reverse proxy
6. Extract token usage from response
7. Record usage event asynchronously (fire-and-forget)

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

## Deployment Memory

Dev Kubernetes image delivery is owned by Argo CD Image Updater, not GitHub Actions.

When recreating or bootstrapping the cluster:

```powershell
# Apply argo apps
# kubectl apply -f infra/argocd/orr-dev-app.yaml
```

Then verify:

```powershell
kubectl get applications -n argocd
```

Expected Argo CD apps include `orr-infra`, and `orr-services`.

Keep the ownership boundary clear:

- GitHub Actions only restores, builds, tests, and pushes GHCR images with `sha-<commit>` and `dev-latest` tags.
- Argo CD Image Updater watches `dev-latest`, resolves digests, writes image overrides, and lets Argo CD sync `orr-services`.
- Do not reintroduce a GitHub Actions job that commits image tags into Kustomize manifests.
