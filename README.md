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

## 9Router node setup in Kubernetes

This section describes how to register a new 9Router (f9router) node in the local kind cluster and connect it to ORR Control Plane.

### Step 1 — Create the f9router Kubernetes Secret

The `f9router-secret` Secret provides runtime configuration to the f9router container. It must exist before the pod starts.

```powershell
# Generate a random control token (keep it — you will need it in steps 3 and 4)
$controlToken = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 40 | ForEach-Object { [char]$_ })

kubectl create secret generic f9router-secret -n orr-dev `
  --from-literal=ROUTER_CONTROL_TOKEN="$controlToken" `
  --from-literal=OPENROUTER_API_KEY="<your-openrouter-key>" `
  --dry-run=client -o yaml | kubectl apply -f -
```

`ROUTER_CONTROL_TOKEN` is an arbitrary secret string you generate yourself. It authenticates ORR Control Plane to the f9router admin API (`/api/integrations/*`). Store it somewhere safe — you will enter this exact value in the ORR admin panel.

### Step 2 — Verify the f9router pod is running

```powershell
kubectl get pods -n orr-dev -l app=f9router
kubectl logs -n orr-dev -l app=f9router --tail=20
```

The readiness probe calls `GET /api/health` and expects HTTP 200. Wait until the pod status is `Running (1/1)`.

### Step 3 — Determine the node endpoint URL

**Use the Kubernetes Service DNS (recommended for persistent registrations):**

```
http://f9router/
```

The `f9router` Service listens on port 80 and forwards traffic to the container port 20128.

**For debugging only — use the pod IP:**

```powershell
kubectl get pod -n orr-dev -l app=f9router -o jsonpath='{.items[0].status.podIP}'
# Use as: http://<pod-ip>:20128/
```

> Warning: pod IPs change on every pod restart. Always use the Service DNS for registered nodes.

### Step 4 — Register the node in the Admin UI

1. Open `http://localhost/admin/nodes`
2. Click **Add Node**
3. Fill in:
   - **Display Name** — e.g. `Local Dev f9router`
   - **Endpoint URL** — `http://f9router/` (or pod IP for debugging)
   - **Control Token** — the **actual value** of `ROUTER_CONTROL_TOKEN` from Step 1 (not an alias name, the raw token string)
4. Click **Add**

> Critical: the Control Token field must contain the real token value. ORR Control Plane (`RouterNodeProvider`) sends it as `Authorization: Bearer <value>` directly to the f9router admin API.

### Step 5 — Persist the webapp model-listing token in git

The WebApp admin panel fetches the model list from the node using a `sk-or-*` user API key (f9router requires this format for `/v1/models` from non-localhost IPs). You need to create a key and configure the mapping.

**a) Create an API key in the WebApp** (Workspace → API Keys → Create).

**b) Push the key to the node manually if it was created before node registration:**

```powershell
kubectl exec -n orr-dev deploy/control-plane -- wget -qO- \
  --header="Authorization: Bearer $controlToken" \
  --header="Content-Type: application/json" \
  --post-data='{"key":"sk-or-<key>","name":"admin-seed"}' \
  http://f9router/api/integrations/keys
```

**c) Add the token mapping to the webapp kustomize patch** (`infra/kustomize/overlays/dev/services/webapp-env-patch.yaml`):

```yaml
env:
  - name: RouterNodeTokens__<ROUTER_CONTROL_TOKEN_VALUE>
    value: "sk-or-<api-key-value>"
```

Replace `<ROUTER_CONTROL_TOKEN_VALUE>` and `<api-key-value>` with the actual values. The `__` separator maps to `:` in ASP.NET Core configuration.

**d) Commit and push.** ArgoCD (`selfHeal: true`) reverts any `kubectl set env` changes that are not in git.

### Step 6 — Verify the health check path

Control Plane health-checks nodes via `GET /api/health` (no auth required). The base kustomize default is `/v1/models` (requires auth and fails). Ensure the dev overlay patch is in place:

```powershell
# Check current effective value
kubectl get deployment -n orr-dev control-plane `
  -o jsonpath='{.spec.template.spec.containers[0].env}' `
  | ConvertFrom-Json | Where-Object name -eq RouterHealthChecks__HealthPath
```

Must be `/api/health`. The patch is in `infra/kustomize/overlays/dev/services/control-plane-env-patch.yaml`.

### Step 7 — Confirm the node becomes Healthy

```powershell
# Watch node status in the DB
kubectl exec -n orr-dev deploy/control-plane -- wget -qO- http://localhost:8080/health
```

Or open `http://localhost/admin/nodes` — the node status should change to **Healthy** within 30–60 seconds.

---

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
