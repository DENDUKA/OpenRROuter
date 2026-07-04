# AGENTS.md

> **IMPORTANT**: ALWAYS respond to the user in Russian (Всегда отвечай на русском языке).

Compact guide for agents working in this repo. Each fact below is non-obvious
and was learned from config/code, not from generic .NET knowledge.

## Build & Test

```powershell
# Restore + build + test (the -nr:false -m:1 flags are mandatory in this repo:
# some Windows sandboxes throw opaque MSBuild node-reuse errors without them)
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build  OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test   OpenRussiaRouter.sln --no-build   -nr:false -m:1

# Single test project
dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1

# Single test by name
dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -filter "FullyQualifiedName~MyTest"
```

Run services locally: `dotnet run --project src/OpenRussiaRouter.<Service>`.

### Build will fail on any warning

`Directory.Build.props` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
and `<AnalysisLevel>latest</AnalysisLevel>`. Fix warnings before committing,
don't suppress them ad-hoc. NuGet `NU19xx` (transitive dependency) warnings
are exempt and won't fail the build.

### Central Package Management

`Directory.Packages.props` sets `<ManagePackageVersionsCentrally>true</ManagePackageVersion>`.
**Never put `<PackageVersion>` in a `.csproj`** — all versions live in
`Directory.Packages.props`. Adding a new package requires editing both files.

### Test stack

- xUnit + FluentAssertions + Moq
- **PostgreSQL integration tests use Testcontainers** — they auto-start a
  Docker postgres container; opt-out gracefully when Docker isn't available.
  Env vars `ORR_{AUTH|BILLING|CONTROL}_POSTGRES_TEST_CONNECTION` only override
  the connection string, they don't enable the tests.
- **E2E tests are Playwright, opt-in**:
  ```powershell
  pwsh tests/OpenRussiaRouter.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
  # config: tests/OpenRussiaRouter.E2ETests/e2e.local.json (gitignored)
  ```

### pre-push hook builds Docker images

`.githooks/pre-push` triggers `build-push.ps1` on `git push` to `main`, which
builds and pushes changed service images to GHCR. One-time setup:
`git config core.hooksPath .githooks`. The hook never blocks the push
(it `exit 0`s on failure) — verify image builds separately.

## Architecture

OpenAI-compatible LLM gateway with $ billing, .NET 8 microservices over gRPC.
**No Entity Framework anywhere** — Dapper + Npgsql only. Don't add EF packages,
`DbContext`, or EF migrations.

### Services (all under `src/`)

| Project | Role | Port |
|---|---|---|
| `OpenRussiaRouter.ApiGateway` | Public OpenAI-compatible HTTP entry | HTTP 5000 |
| `OpenRussiaRouter.AuthService` | Users, sessions, API key lifecycle | gRPC 5001 |
| `OpenRussiaRouter.BillingService` | Balance ledger, model catalog, usage | gRPC 5002 |
| `OpenRussiaRouter.ControlPlane` | 9Router node registry, health, leases | gRPC 5003 |
| `OpenRussiaRouter.WebApp` | **Razor Pages** cabinet + admin (NOT Blazor) | HTTP 5004 |
| `OpenRussiaRouter.CollectorService` | Polls nodes/OpenRouter, caches, exposes snapshots | gRPC 5005 |
| `OpenRussiaRouter.BuildingBlocks` | Shared primitives (Money, TokenUsage, errors, migrations) | — |
| `OpenRussiaRouter.Contracts` | DTOs + `.proto` files | — |
| `OpenRussiaRouter.9Router` | Fork of 9Router (router core); treat as upstream, has its own CLAUDE.md | — |

Each service also has its own `CLAUDE.md` with deeper, service-specific
conventions — read it before editing that service.

### API Gateway request flow (`POST /v1/chat/completions`)

1. Bearer token → validate via AuthService gRPC
2. Balance + model availability via BillingService gRPC (resolves user-facing
   `ModelId` like `ORR/Gemini-3.1-Pro` → internal `RouterModelId`)
3. Select node via ControlPlane gRPC (capability-aware, falls back to least-load)
4. Patch JSON: replace `model` with `RouterModelId`; if either model id contains
   "gemini" (case-insensitive), apply `GeminiSchemaPatcher` (inlines `$ref`/`$defs`
   in tool/function schemas — Gemini rejects `$ref`)
5. Forward via YARP (non-streaming) or `HttpCompletionOption.ResponseHeadersRead`
   (streaming). **Retry once** to a different node on 5xx/network error before
   the first byte reaches the client.
6. Extract usage from JSON body or final SSE chunk (`include_usage`)
7. Fire-and-forget usage event to BillingService; release the lease

Endpoints: `/v1/chat/completions`, `/v1/chat/completions/stream` (explicit
stream alias), `/v1/models`, `/health`, `/ready`.

### Patterns every service shares

- **Repository**: `I{Service}Repository` with Dapper/Postgres + in-memory impls
  (in-memory is used in unit tests).
- **DI registration**: `Add{Service}Infrastructure()` /
  `Add{Service}Application()` extension methods in `Infrastructure/Startup/`.
- **SQL migrations**: raw `.sql` files in `db/migrations/{auth|billing|control|gateway}/`,
  auto-applied on startup via `PostgresSqlMigrationRunner`. Add a new file with
  the next numeric prefix (e.g. `021_*.sql` in billing).
- **gRPC contracts**: `.proto` in `src/OpenRussiaRouter.Contracts/Protos/`
  (`auth.proto`, `billing.proto`, `control-plane.proto`).
- **Correlation IDs**: `X-Correlation-Id` header flows through
  `CorrelationIdReader` into Serilog context.

## gRPC services config

Service URLs live in each consumer's `appsettings.json` under `GrpcServices`:
`Auth` → `http://localhost:5001`, `Billing` → `:5002`, `ControlPlane` → `:5003`.

## 9Router integration — do not confuse these tokens

Two unrelated token types both flow through ControlPlane:

1. **Control Token** (per node). Stored in `control.router_nodes.control_token`
   as the **raw token value**, not a secret name. Sent verbatim as
   `Authorization: Bearer <control_token>` to the node's admin API
   (`/api/integrations/*`). `RouterNodeProvider` has no config-based resolution.
2. **User API keys** (`sk-or-*`). Created in WebApp, hashed in AuthService,
   pushed to nodes on create, deactivated on revoke via
   `control.integration_key_sync` mapping.

> The DB column used to be documented as a "secret name" — that was wrong.
> Don't reintroduce `RouterHealthChecks:NodeTokens:{name}` resolution.

## Deployment — keep this ownership boundary

- **GitHub Actions** (`.github/workflows/build-deploy.yml`): restore, build,
  test, push images to `ghcr.io/denduka/<service>` with `sha-<commit>` and
  `dev-latest` tags. Skips unchanged services via path detection.
- **Argo CD Image Updater** (in cluster) watches `dev-latest`, resolves
  digests, writes image overrides, lets Argo CD sync `orr-services`.
- **Never** add a GHA job that commits image tags into Kustomize manifests
  or calls Argo CD directly. Image Updater owns that.

Expected Argo CD apps after bootstrap: `orr-infra`, `orr-services`,
`orr-argocd-image-updater` (defined in `infra/argocd/`).

Argo CD runs with `selfHeal: true` on dev → **`kubectl set env` /
`kubectl edit` changes are reverted within ~1 min**. All env changes must go
through `infra/kustomize/overlays/dev/services/*-env-patch.yaml` + git.

### Known infra gotchas

- 9Router node health check: dev overlay must set
  `RouterHealthChecks__HealthPath=/api/health`. The base default `/v1/models`
  returns 401 from f9router and the node never becomes Healthy.
- Full local k8s runbook (kind, Argo CD, node registration, secrets):
  `docs/argo-k8s-dev-runbook.md` and `README.md`.
