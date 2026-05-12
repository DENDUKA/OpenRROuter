# Deployment and CI/CD Design

Date: 2026-05-12
Project: OpenRussiaRouter
Status: Approved design for implementation planning

## Goal

Deploy OpenRussiaRouter services to a local/lab Kubernetes environment using Argo CD GitOps, GitHub Actions, GHCR images, Seq logging, and automatic redeploys when `master` changes.

The design targets the first dev environment only. It should stay simple enough for lab use while keeping the path to production clear.

## Context

OpenRussiaRouter is a .NET microservice system with these deployable services:

- `OpenRussiaRouter.ApiGateway`
- `OpenRussiaRouter.AuthService`
- `OpenRussiaRouter.BillingService`
- `OpenRussiaRouter.ControlPlane`
- `OpenRussiaRouter.WebApp`

The system uses PostgreSQL with service-owned data boundaries. Existing SQL migrations live under per-service folders in `db/migrations`. The project intentionally uses Dapper and Npgsql instead of Entity Framework.

The 9Router fork is a Next.js application with its own Docker support, but for this MVP design 9Router nodes remain outside Kubernetes. The Router Control Plane stores external node endpoints and token references.

## Decisions

- Kubernetes target: local/lab cluster first.
- CI: GitHub Actions.
- Container registry: GHCR.
- Deployment model: GitOps with Argo CD auto-sync.
- Logging: Seq inside Kubernetes.
- Environment scope: one `dev` environment.
- Secrets: SOPS-encrypted manifests in Git.
- Rollback: Git revert of deployment manifest/image tag changes.

## Recommended Approach

Use a GitOps deploy path watched by Argo CD. GitHub Actions builds and tests the application, publishes images to GHCR, then updates image tags in the deploy manifests. Argo CD observes the Git change and applies it to Kubernetes automatically.

This keeps cluster credentials out of CI, makes deploy state auditable in Git, and gives rollback through normal Git operations.

## Alternatives Considered

### CI-driven Argo sync

GitHub Actions could build images and call Argo CD directly through the Argo API or CLI.

Benefits:
- Simple mental model.
- Immediate sync after CI finishes.

Rejected because:
- CI needs Argo credentials.
- CI becomes coupled to the cluster.
- Rollback is less clean than reverting GitOps state.

### Manual lab GitOps

GitHub Actions could build and push images only, while humans update manifests and manually sync Argo.

Benefits:
- Lower initial automation risk.
- Useful for early experiments.

Rejected because:
- It does not satisfy automatic redeploy on `master` changes.
- It creates manual drift between image state and deploy state.

## Architecture

The repository will contain a deploy path such as `infra/` with Kubernetes manifests or Helm/Kustomize configuration for the dev environment. The implementation plan can choose the exact templating tool, but the design expects one clear deploy entrypoint for Argo CD.

Argo CD runs in the `argocd` namespace and owns one application named `orr-dev`. That application watches the deploy path on `master` and applies resources to the `orr-dev` namespace.

Runtime resources in `orr-dev` include:

- Deployments for `api-gateway`, `auth-service`, `billing-service`, `control-plane`, and `webapp`.
- StatefulSet for `postgres`.
- StatefulSet for `seq`.
- Migration Jobs for service database migrations.
- Services for HTTP and gRPC traffic.
- Ingress for the public API Gateway and WebApp.
- Optional restricted ingress for Seq.
- ConfigMaps for non-secret settings.
- Secrets generated from SOPS-encrypted source files.

Container images use GHCR names and immutable commit tags:

```text
ghcr.io/OWNER/openrussiarouter-api-gateway:sha-COMMIT_SHA
ghcr.io/OWNER/openrussiarouter-auth-service:sha-COMMIT_SHA
ghcr.io/OWNER/openrussiarouter-billing-service:sha-COMMIT_SHA
ghcr.io/OWNER/openrussiarouter-control-plane:sha-COMMIT_SHA
ghcr.io/OWNER/openrussiarouter-webapp:sha-COMMIT_SHA
```

An optional `master-latest` tag may exist for convenience, but Kubernetes manifests should use immutable SHA tags.

## CI/CD Flow

On push or merge to `master`, GitHub Actions runs the deployment pipeline:

1. Restore the .NET solution using the repository NuGet configuration.
2. Build the solution.
3. Run tests.
4. Build Docker images for each deployable .NET service.
5. Push images to GHCR with `sha-COMMIT_SHA` tags.
6. Optionally push `master-latest` tags.
7. Update the deploy values or manifests with the new immutable image tags.
8. Commit the deploy manifest change back to the watched Git path or deploy branch.
9. Let Argo CD auto-sync the resulting Git change.

The CI pipeline must not hold Kubernetes or Argo CD credentials. The cluster is responsible for pulling GHCR images through an image pull secret created during bootstrap.

## Argo CD Behavior

Argo CD watches the deploy path and keeps `orr-dev` synchronized with Git.

Expected settings:

- Auto-sync enabled.
- Self-heal enabled.
- Prune enabled only for resources owned by the `orr-dev` application.
- Sync waves or hooks used so migration Jobs finish before app Deployments roll forward.

If a rollout fails, Argo CD marks the application as degraded. Recovery is performed by reverting the Git commit that changed image tags or manifests.

## Kubernetes Layout

Namespaces:

- `argocd`: Argo CD installation and controllers.
- `orr-dev`: OpenRussiaRouter runtime resources.

Workloads:

- `api-gateway`: public OpenAI-compatible API entrypoint.
- `auth-service`: users, sessions, invites, API keys.
- `billing-service`: balance, usage, ledger, pricing.
- `control-plane`: external 9Router node registry and selection.
- `webapp`: Blazor user/admin UI.
- `postgres`: dev database host.
- `seq`: structured log store and UI.

Networking:

- API Gateway and WebApp receive ingress traffic.
- Internal service-to-service calls stay inside `orr-dev`.
- gRPC services are exposed only through internal Kubernetes Services.
- Seq is internal by default. If exposed, ingress must be restricted and authenticated.

NetworkPolicies should start from the required traffic paths:

- Gateway and WebApp can call internal services.
- Services can call their required PostgreSQL database/schema.
- Services can send logs to Seq.
- Default-deny can be enabled after basic connectivity is validated.

Health and resources:

- Every .NET service has readiness and liveness probes.
- Startup probes are allowed for slower services.
- Every workload has requests and limits.
- Values are sized for lab use first and can be increased later.

## PostgreSQL and Migrations

The dev environment can use one PostgreSQL instance with separate service-owned databases or schemas for:

- Auth
- Billing
- Control Plane

Direct cross-service database joins remain prohibited.

Migration Jobs execute explicit SQL scripts from the existing migration directories:

- `db/migrations/auth`
- `db/migrations/billing`
- `db/migrations/control`

Migration Jobs must complete before dependent application Deployments roll out. A failed migration blocks the rollout and leaves the previous application version running where possible.

## Secrets and Configuration

Non-secret configuration belongs in ConfigMaps or deploy values:

- Service URLs.
- Ports.
- Environment name.
- Feature flags.
- Seq endpoint.

Secrets belong in SOPS-encrypted files committed to Git:

- PostgreSQL passwords.
- JWT/session/API signing secrets.
- Seq admin password or ingestion credentials.
- 9Router node token references when they are represented in Kubernetes.

The SOPS private key or Age identity is never committed. The implementation plan must define how Argo CD decrypts secrets for the lab cluster.

The GHCR image pull secret is cluster bootstrap state. It is not owned by application charts unless the implementation explicitly chooses to model bootstrap resources in Git.

## Logging with Seq

All .NET services send structured logs to the in-cluster Seq ingestion endpoint.

Every service should enrich logs with:

- `service`
- `env`
- `version`
- `commitSha`
- `correlationId`
- `requestId`

Logs should include operational metadata such as HTTP/gRPC status, latency, and application error codes.

Logs must not include:

- Prompt bodies.
- Response bodies.
- API keys.
- Provider credentials.
- Router node tokens.
- Session/JWT secrets.

Seq runs with persistent storage and dev retention limits. A 7-14 day retention target is acceptable for the first lab environment, or an equivalent size-based limit if that is more practical with the chosen Seq configuration.

Seq unavailability must not crash request handling. Logging delivery failures should be non-fatal.

External 9Router nodes do not need to send logs to Seq in the first implementation. A later iteration can add restricted Seq ingestion over VPN or another protected path.

## Error Handling and Rollback

Pipeline failures:

- Failed restore, build, or tests stop the pipeline before image push.
- Failed image build or push stops deploy manifest updates.
- Failed deploy manifest update stops before Argo sees a new desired state.

Runtime failures:

- Failed migration blocks dependent rollout.
- Failed readiness keeps a new pod out of service.
- Failed rollout makes the Argo application degraded.
- Seq outage is non-fatal to application request handling.

Rollback:

- Revert the Git commit that changed image tags or deploy manifests.
- Argo CD applies the reverted desired state.
- Database rollback is not automatic. Migration design should prefer backward-compatible changes once production environments exist.

## Testing and Acceptance Criteria

The deployment design is accepted when:

- A push to `master` can build, test, package, and publish all deployable .NET service images to GHCR.
- The deploy path records immutable image tags for the selected commit.
- Argo CD auto-syncs `orr-dev` from Git without CI using cluster credentials.
- API Gateway and WebApp become reachable through ingress.
- Internal gRPC calls resolve through Kubernetes Services.
- Migration Jobs run before app Deployments.
- Services emit structured logs into Seq.
- Prompt bodies, response bodies, API keys, and secrets are absent from logs.
- Failed tests prevent deploy.
- Failed readiness or migration produces a visible Argo degraded/failed state.
- Git revert rolls back image tags and causes Argo to restore the previous desired state.

## Non-goals

- Production-grade multi-environment deployment.
- Public 9Router node deployment inside Kubernetes.
- Real payment provider infrastructure.
- Full external secret manager integration.
- HA PostgreSQL.
- HA Seq.
- Centralized metrics/tracing stack beyond Seq logs.
- Public anonymous Seq access.

## Open Implementation Choices

The implementation plan must choose:

- Helm vs Kustomize for the deploy path.
- Exact local/lab Kubernetes distribution.
- Exact Argo CD SOPS integration mechanism.
- Exact GHCR organization and image names.
- Whether deploy updates commit directly to `master` or to a dedicated deploy branch/path.

These are implementation details, not blockers for the approved design.
