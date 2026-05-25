# Argo CD Image Updater Delivery Design

Date: 2026-05-25
Project: OpenRussiaRouter
Status: Approved design for implementation planning

## Goal

Make Argo CD Image Updater the single owner of automatic container delivery for the dev Kubernetes environment.

GitHub Actions should test, build, and publish service images to GHCR. The cluster-side Argo CD Image Updater should detect new image digests, write the selected digest back to Git, and let Argo CD sync the updated desired state.

## Context

The repository already contains two competing image delivery mechanisms:

- `.github/workflows/build-deploy.yml` builds images and also has an `update-manifests` job that commits `sha-*` tags into `infra/kustomize/overlays/dev/services/kustomization.yaml`.
- `infra/argocd/orr-dev-app.yaml` annotates the `orr-services` Argo CD Application for Argo CD Image Updater.
- `infra/argocd-image-updater/` defines a separate `orr-argocd-image-updater` Application and controller configuration.

The desired model is approach A: GitHub Actions publishes images, while Argo CD Image Updater watches GHCR and owns the deploy image state.

## Decision

Use Argo CD Image Updater as the only automatic writer of deployment image versions.

GitHub Actions remains responsible for:

- Restore, build, and test.
- Detecting which service images need rebuilding.
- Building and pushing changed service images to GHCR.
- Publishing both immutable `sha-<commit>` tags and mutable `dev-latest` tags.

GitHub Actions must not update Kubernetes image manifests after the image push.

Argo CD Image Updater remains responsible for:

- Polling GHCR for the image aliases listed on the `orr-services` Application.
- Resolving `dev-latest` to a concrete digest.
- Writing the selected digest to Git through its configured git write-back method.
- Allowing Argo CD to sync the Git change into `orr-dev`.

## Architecture

The delivery path is:

```text
push to main
  -> GitHub Actions Build Images workflow
     -> dotnet restore/build/test
     -> Docker build and push changed services
     -> ghcr.io/denduka/<service>:sha-<commit>
     -> ghcr.io/denduka/<service>:dev-latest
  -> Argo CD Image Updater polls GHCR
     -> detects new dev-latest digest
     -> commits Argo CD source parameter override to Git
  -> Argo CD sees Git change
     -> syncs orr-services
     -> Kubernetes performs rolling updates
```

`orr-argocd-image-updater` stays as a separate Argo CD Application. That keeps the updater itself managed by GitOps while preserving a clear boundary: it updates image parameters for `orr-services`, not the core service manifests directly.

## Components

### GitHub Actions

`.github/workflows/build-deploy.yml` should keep the `test` and `build-image` jobs. The `update-manifests` job should be removed because it writes image tags into the same desired state that Image Updater is supposed to own.

`.github/workflows/cd.yml` should be removed. It is informational only, and its `Deploy` name makes the ownership model less clear because GitHub Actions does not deploy to Kubernetes in this design.

### Argo CD Services Application

`infra/argocd/orr-dev-app.yaml` should keep the `orr-services` Image Updater annotations:

- `image-list` with the five OpenRussiaRouter services and `f9router`.
- `*.update-strategy: digest`.
- `write-back-method: git:secret:argocd/repo-openrussiarouter`.

The `orr-services` Application continues to watch `infra/kustomize/overlays/dev/services`.

### Kustomize Services Overlay

`infra/kustomize/overlays/dev/services/kustomization.yaml` should provide sensible baseline image tags, preferably `dev-latest`, for all Image Updater managed services.

The actual deployed immutable image should come from the Image Updater git write-back file, which records digest-pinned overrides. This keeps the base overlay readable while preserving exact deployed state in Git.

### Image Updater Application

`infra/argocd/orr-image-updater-app.yaml` and `infra/argocd-image-updater/` remain the cluster-side automation entrypoint.

The updater uses:

- `interval: 2m`.
- GHCR credentials from `pullsecret:argocd/ghcr-pull-secret`.
- Git identity from `argocd-image-updater-config`.
- Git write access through `argocd/repo-openrussiarouter`.

## Error Handling

If GitHub Actions restore, build, tests, or image push fails, no image update is available for Image Updater to deploy.

If Image Updater cannot read GHCR, it should log the registry error and leave the currently deployed digest unchanged.

If Image Updater cannot write to Git, Argo CD should keep syncing the existing desired state. The fix is to repair repository credentials or write permissions.

If the new image crashes or fails readiness, Kubernetes rollout and Argo CD health should expose the degraded state. Rollback is performed through Git by reverting the Image Updater write-back commit or by pinning a previous digest through the same GitOps path.

## Security

GitHub Actions does not need Kubernetes or Argo CD credentials.

The Argo CD repository credential used by Image Updater needs repository read/write access because it writes back image digest overrides.

GHCR pull credentials stay in Kubernetes secrets:

- `orr-dev/ghcr-pull-secret` for workload image pulls.
- `argocd/ghcr-pull-secret` for Image Updater registry reads.

No PATs, registry credentials, or Argo CD credentials should be committed to the repository.

## Testing And Acceptance Criteria

The design is accepted when:

- A push to `main` runs tests and publishes changed service images to GHCR.
- The Build Images workflow no longer commits image tag changes into `kustomization.yaml`.
- Argo CD Image Updater detects new `dev-latest` digests and writes digest-pinned state to Git.
- Argo CD syncs `orr-services` from that Git state.
- Deployed pod images resolve to immutable digests.
- A failed test or failed image push prevents any automatic deployment.
- Git history has one clear source of deploy image updates: Image Updater write-back commits.

## Non-goals

- Adding production environments.
- Replacing Kustomize with Helm.
- Moving secrets into an external secret manager.
- Calling the Argo CD API from GitHub Actions.
- Implementing manual promotion between environments.

## Implementation Notes

The implementation plan should:

1. Remove the `update-manifests` job from `.github/workflows/build-deploy.yml`.
2. Delete `.github/workflows/cd.yml`.
3. Normalize baseline tags in `infra/kustomize/overlays/dev/services/kustomization.yaml` to `dev-latest` for Image Updater managed services.
4. Verify Image Updater annotations still match the image names pushed by CI.
5. Update `docs/argo-k8s-dev-runbook.md` so it describes only the Image Updater delivery path and does not mention GitHub Actions manifest commits as the normal flow.
