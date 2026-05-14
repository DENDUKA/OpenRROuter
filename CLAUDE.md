# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

OpenRussiaRouter is a .NET 8 OpenAI-compatible LLM gateway with RUB billing, invite-only auth, API keys, and routing of upstream LLM traffic through a pool of external 9Router proxy nodes. It is a multi-service solution (`OpenRussiaRouter.sln`) where services talk over gRPC and each owns its PostgreSQL schema. The product spec lives in [docs/prd/open-russia-router-prd.md](docs/prd/open-russia-router-prd.md); the technical backlog and per-task status is in [TODO.md](TODO.md).

## Development environment

Day-to-day development is expected to happen on a **remote dev environment** in a supported country (VPS in DE/NL/etc., GitHub Codespaces, or similar), with the local laptop acting as a thin client over SSH. The current repository checkout snapshot is Windows, but Linux remote shells are the primary target — write any new scripts/tooling cross-platform (use forward-slash paths in cross-cutting docs, prefer `dotnet`-native tooling over PowerShell-only commands when both work, keep secrets / connection strings out of any shell history examples). When the local OS is Windows, the `-nr:false -m:1` MSBuild flags below remain mandatory because of the Windows MSBuild node-reuse issue.

## Build, test, run

The repo uses `dotnet` against the solution file. The `-nr:false -m:1` flags are deliberate — some sandboxed Windows environments fail MSBuild node reuse / parallel restore without a useful error. Do not drop them when running from this repo.

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build   OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test    OpenRussiaRouter.sln --no-build  -nr:false -m:1
```

Single test project / single test:

```powershell
dotnet test tests\OpenRussiaRouter.AuthService.Tests\OpenRussiaRouter.AuthService.Tests.csproj --no-build -nr:false -m:1
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1 --filter "FullyQualifiedName~LoginServiceTests.Login_returns_session"
```

`Directory.Build.props` sets `TreatWarningsAsErrors=true` and `EnforceCodeStyleInBuild=true` solution-wide. Warnings will break the build. The SDK version is pinned in `global.json` (8.0.420, `rollForward: latestFeature`). Package source is locked to nuget.org via `NuGet.Config` — pass `--configfile NuGet.Config` on restore so sandboxed runs don't pick up an unexpected source.

Run a service locally: `dotnet run --project src/OpenRussiaRouter.<Name>`. With no `ConnectionStrings:*Db` value present each service falls back to an in-memory implementation, so the whole solution can be exercised without PostgreSQL.

## Architecture

Services and their roles (each is its own ASP.NET Core project under `src/`):

- **ApiGateway** — public OpenAI-compatible surface (`/v1/models`, `/v1/chat/completions`). Stateless; calls Auth / Billing / Control Plane over gRPC, then proxies HTTPS to a leased 9Router node. Single end-to-end request flow is documented as a sequence diagram in the PRD.
- **AuthService** — users, invites, password login, sessions, API keys. Exposes gRPC `ValidateApiKey`, `CreateInvite`, `CompleteInvite`, `CreateApiKey`, `RevokeApiKey`, `Login`.
- **BillingService** — append-only RUB ledger, virtual top-ups, model pricing catalog, usage events. Exposes `CheckBalanceAndModel`, `RecordUsage`, `ListPublicModels`, `GetBalance`, `CreateVirtualTopUp`, `UpsertModelPrice`, `ListUsage`, `ListBillingAnomalies`.
- **ControlPlane** — registry of external 9Router nodes, health-check worker, least-load node selection, lease/release with TTL cleanup. Exposes `SelectRouterNode`, `ReleaseRouterNode`, `RegisterRouterNode`, `DisableRouterNode`, `ListRouterNodes`.
- **WebApp** — Blazor Server cabinet + admin dashboard. Holds gRPC client channels to the three backend services (URLs from `GrpcServices:Auth|Billing|ControlPlane`, defaults `http://localhost:5001|5002|5003`).
- **BuildingBlocks** — shared primitives only: `ApiError`/`ErrorCodes`, `ServiceHealth`, `Currency`/`MoneyAmount`, `TokenUsage`/`ModelTokenPrice`/`UsageCostCalculator`, `CorrelationIds`. Keep this assembly free of service dependencies.
- **Contracts** — `.proto` files (`Protos/auth.proto`, `billing.proto`, `control-plane.proto`) plus internal C# DTOs. Service projects reference the `.proto` files via `<Protobuf Include="..\OpenRussiaRouter.Contracts\Protos\*.proto">` with `GrpcServices="Both"` (server) or `"Client"` (consumer). The contracts project itself is the canonical home of the proto sources.

### Persistence — Dapper only, no EF

PostgreSQL access is **Dapper + Npgsql**. EF Core, `DbContext`, EF migrations, and EF-specific test packages are intentionally absent and must not be added. Schema lives in plain SQL under `db/migrations/{auth,billing,control}/`.

Each service uses the same pattern in `Program.cs`: if `ConnectionStrings:<Service>Db` is set, register the `Npgsql...ConnectionFactory` + `Dapper...Repository`; otherwise register the `InMemory...Repository`. This is what lets `dotnet run` and the unit suite work without Postgres while the same code path exercises the real DB when configured.

### Tests

Five xUnit projects under `tests/`, one per service plus `BuildingBlocks.Tests`. PostgreSQL persistence tests are opt-in two ways:

1. **Testcontainers** (default for AUTH/BILLING/CONTROL persistence tests) — spins up Postgres in Docker; silently no-ops when Docker is unavailable. No env var needed.
2. **External connection string** — set one shared `ORR_POSTGRES_TEST_CONNECTION` or per-service `ORR_AUTH_POSTGRES_TEST_CONNECTION` / `ORR_BILLING_POSTGRES_TEST_CONNECTION` / `ORR_CONTROL_POSTGRES_TEST_CONNECTION`.

If neither is available the unit suite still runs to completion and stays self-contained — do not fail or skip-with-error in that case; existing tests already handle the no-op path.

### The 9Router subdirectory

`src/OpenRussiaRouter.9Router` is a **separate Next.js project — a vendored fork of upstream 9Router**, not part of the .NET solution. It has its own [AGENTS.md](src/OpenRussiaRouter.9Router/AGENTS.md) with its own setup, ports, and conventions; read that file before touching anything inside that directory. Edits there should not leak out into the .NET services and vice versa.

## Conventions worth knowing

- Service entry points are `partial class Program;` so integration tests can `WebApplicationFactory<Program>` them. Preserve that declaration when editing `Program.cs`.
- Public errors leaving the Gateway use the OpenAI-compatible error shape with `type`, `code`, `message`, `request_id` plus an `X-Request-Id` correlation header. Internal exception details must not leak. See `BuildingBlocks/Errors/ApiError.cs` and `BuildingBlocks/Telemetry/CorrelationIds.cs`.
- Money is `BuildingBlocks/Money/MoneyAmount` — use it rather than raw decimals when crossing service / billing boundaries.
- Don't mark TODO items in [TODO.md](TODO.md) as `[x]` unless the work is actually merged; the doc is the source of truth for MVP scope and is regularly diffed against the PRD.
