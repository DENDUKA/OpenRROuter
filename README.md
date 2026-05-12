# OpenRussiaRouter

OpenRussiaRouter is a .NET-based OpenAI-compatible LLM gateway for RUB billing, API keys, 9Router-based routing and pilot B2B usage accounting.

## Current implementation status

Implemented first foundation slice:

- .NET solution and service projects.
- Shared building blocks for errors, correlation ids, health responses, money, token usage and usage cost calculation.
- Shared internal contracts and initial `.proto` files for Auth, Billing and Router Control Plane.
- Service skeletons for API Gateway, Auth Service, Billing/Usage Service, Router Control Plane and Blazor WebApp.
- Basic Gateway stubs for `/v1/models` and `/v1/chat/completions`.
- Auth domain foundation: invites, password hashing, login sessions, API key lifecycle and Auth `ValidateApiKey` gRPC service.
- Persistence layer direction: PostgreSQL is accessed with Dapper + Npgsql. Entity Framework is intentionally not used in this project.
- Gateway/Control Plane integration for non-streaming 9Router proxy path is in progress.
- xUnit tests for money and usage cost calculation.

## Projects

- `src/OpenRussiaRouter.ApiGateway` - public OpenAI-compatible API entrypoint.
- `src/OpenRussiaRouter.AuthService` - users, invites, sessions and API keys.
- `src/OpenRussiaRouter.BillingService` - balance, ledger, pricing and usage events.
- `src/OpenRussiaRouter.ControlPlane` - 9Router node registry, health and node selection.
- `src/OpenRussiaRouter.9Router` - рабочий fork 9Router. В этой папке есть `AGENTS.md` с описанием проекта и правилами работы с fork.
- `src/OpenRussiaRouter.WebApp` - Blazor user cabinet and admin dashboard.
- `src/OpenRussiaRouter.BuildingBlocks` - shared primitives.
- `src/OpenRussiaRouter.Contracts` - internal service contracts and `.proto` files.

## Build and test

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

`-nr:false -m:1` is used because some sandboxed Windows environments can fail MSBuild node reuse/parallel restore without a useful error message.

## Persistence

All .NET services that use PostgreSQL must use Dapper over Npgsql. Do not add Entity Framework packages, `DbContext`, EF migrations or EF-specific tests. SQL schema/migration work should be implemented as explicit SQL scripts or a migration runner chosen for Dapper-based projects.

Initial SQL migrations live in:

- `db/migrations/auth/001_initial.sql`
- `db/migrations/billing/001_initial.sql`
- `db/migrations/control/001_initial.sql`

PostgreSQL/Dapper integration tests are opt-in. Set one shared connection string with `ORR_POSTGRES_TEST_CONNECTION`, or set service-specific values:

- `ORR_AUTH_POSTGRES_TEST_CONNECTION`
- `ORR_BILLING_POSTGRES_TEST_CONNECTION`
- `ORR_CONTROL_POSTGRES_TEST_CONNECTION`

When these variables are not set, the integration test methods exit without touching an external database; the regular unit suite stays self-contained.
